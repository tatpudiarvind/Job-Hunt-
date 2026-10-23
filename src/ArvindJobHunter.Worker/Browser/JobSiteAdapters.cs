using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Contracts.Api;
using Microsoft.Playwright;

namespace ArvindJobHunter.Worker.Browser;

/// <summary>Selectors for one site, loaded from <c>Browser/sites.json</c> so they can be fixed without a rebuild.</summary>
public sealed record SiteConfig(string Name, string[] Hosts, string ApplyButton, string NextButton, string SubmitButton, string Confirmation, string LoginMarker)
{
    public bool Matches(Uri url) => Hosts.Any(h => h == "*" || url.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}

public sealed record FillResult(IReadOnlyList<FillFieldDto> Fields, IReadOnlyList<string> Unanswered, string? Blocker);

/// <summary>A job site's application form. Fill and verify never submit; only <see cref="SubmitAsync"/> clicks the final button.</summary>
public interface IJobSiteAdapter
{
    string Name { get; }
    bool CanHandle(Uri url);
    Task<FillResult> FillAsync(IPage page, FillPreviewRequest request, CancellationToken cancellationToken);
    Task<string?> VerifyAsync(IPage page);
    Task<SubmitResponse> SubmitAsync(IPage page, CancellationToken cancellationToken);
}

public sealed class AdapterRegistry(IEnumerable<IJobSiteAdapter> adapters)
{
    private readonly IReadOnlyList<IJobSiteAdapter> ordered = adapters.OrderBy(a => a.Name == "company" ? 1 : 0).ToList();

    public IJobSiteAdapter Resolve(Uri url, string? preferred = null) =>
        (preferred is null ? null : ordered.FirstOrDefault(a => a.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
        ?? ordered.First(a => a.CanHandle(url));

    public static AdapterRegistry FromConfig(string path, Func<int, int, Task>? delay = null)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<SitesDocument>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{path} is empty.");
        return new AdapterRegistry(document.Sites.Select(s => (IJobSiteAdapter)new ConfiguredFormAdapter(s, delay)));
    }

    private sealed record SitesDocument(SiteConfig[] Sites);
}

/// <summary>
/// Generic form adapter driven by <see cref="SiteConfig"/>. It types like a person (per-key delays, pauses between
/// fields), walks multi-step forms with the site's Next button, and stops on the review page before Submit.
/// </summary>
public sealed class ConfiguredFormAdapter(SiteConfig config, Func<int, int, Task>? delay = null) : IJobSiteAdapter
{
    private const int MaxSteps = 8;
    private readonly Func<int, int, Task> pause = delay ?? ((min, max) => Task.Delay(Random.Shared.Next(min, max)));

    public string Name => config.Name;
    public bool CanHandle(Uri url) => config.Matches(url);

    public async Task<FillResult> FillAsync(IPage page, FillPreviewRequest request, CancellationToken cancellationToken)
    {
        if (await VerifyAsync(page) is { } blocker) return new FillResult([], [], blocker);
        await ClickIfVisibleAsync(page, config.ApplyButton);
        var fields = new List<FillFieldDto>();
        for (var step = 0; step < MaxSteps; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await VerifyAsync(page) is { } stepBlocker) return new FillResult(fields, [], stepBlocker);
            fields.AddRange(await FillVisibleFieldsAsync(page, request, cancellationToken));
            if (await FindButtonAsync(page, config.SubmitButton) is not null) break;
            if (!await ClickIfVisibleAsync(page, config.NextButton)) break;
        }

        var unanswered = fields.Where(f => f.Required && string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Label).Distinct().ToList();
        if (await FindButtonAsync(page, config.SubmitButton) is null)
            return new FillResult(fields, unanswered, "The final Submit button was not found. Check the open browser window.");
        return new FillResult(fields, unanswered, null);
    }

    public async Task<string?> VerifyAsync(IPage page)
    {
        var captcha = await page.Locator("iframe[src*='captcha'], iframe[title*='challenge' i], .g-recaptcha, .h-captcha, #cf-challenge-running").CountAsync();
        if (captcha > 0) return "A CAPTCHA is shown. Solve it in the open browser window, then prepare again.";
        if (await page.Locator("input[type=password]").CountAsync() > 0)
            return "The site asks you to sign in. Sign in in the open browser window (the session is kept), then prepare again.";
        if (!string.IsNullOrEmpty(config.LoginMarker) && await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex($"^({config.LoginMarker})$", RegexOptions.IgnoreCase) }).CountAsync() > 0
            && await page.Locator("form input:not([type=hidden])").CountAsync() == 0)
            return "The site asks you to sign in. Sign in in the open browser window, then prepare again.";
        return null;
    }

    public async Task<SubmitResponse> SubmitAsync(IPage page, CancellationToken cancellationToken)
    {
        if (await VerifyAsync(page) is { } blocker) return new SubmitResponse(false, null, null, blocker);
        var submit = await FindButtonAsync(page, config.SubmitButton);
        if (submit is null) return new SubmitResponse(false, null, null, "The Submit button is no longer on the page.");
        await pause(400, 1200);
        await submit.ClickAsync();
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 }); } catch (TimeoutException) { }
        var text = await page.Locator("body").InnerTextAsync();
        var match = Regex.Match(text, config.Confirmation, RegexOptions.IgnoreCase);
        return match.Success
            ? new SubmitResponse(true, match.Value, null)
            : new SubmitResponse(false, null, null, "Submit was clicked but no confirmation text was found. Check the browser window before retrying.");
    }

    private async Task<IReadOnlyList<FillFieldDto>> FillVisibleFieldsAsync(IPage page, FillPreviewRequest request, CancellationToken cancellationToken)
    {
        var result = new List<FillFieldDto>();
        foreach (var field in await ScanAsync(page))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = page.Locator($"[data-jh-index='{field.Index}']");
            if (FieldMatcher.IsResumeUpload(field))
            {
                await control.SetInputFilesAsync(request.ResumeFilePath);
                result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), Path.GetFileName(request.ResumeFilePath), field.Required, "resume"));
                continue;
            }
            if (FieldMatcher.IsCoverLetterUpload(field))
            {
                if (string.IsNullOrWhiteSpace(request.CoverLetterFilePath)) { result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), null, field.Required, "skipped")); continue; }
                await control.SetInputFilesAsync(request.CoverLetterFilePath);
                result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), Path.GetFileName(request.CoverLetterFilePath), field.Required, "cover letter"));
                continue;
            }

            var answer = FieldMatcher.Canonical(field.Label) == "Cover letter" ? request.CoverLetterText : FieldMatcher.Answer(field, request.Answers);
            if (string.IsNullOrWhiteSpace(answer))
            {
                result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), null, field.Required, "unknown"));
                continue;
            }

            await pause(250, 900);
            switch (field.Kind)
            {
                case "select":
                    var option = FieldMatcher.PickOption(field.Options, answer);
                    if (option is null) { result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), null, field.Required, "no matching option")); continue; }
                    await control.SelectOptionAsync(new SelectOptionValue { Label = option });
                    answer = option;
                    break;
                case "checkbox":
                case "radio":
                    if (answer.Equals("yes", StringComparison.OrdinalIgnoreCase) || answer.Equals("true", StringComparison.OrdinalIgnoreCase)) await control.CheckAsync();
                    break;
                default:
                    await control.FillAsync("");
                    await control.PressSequentiallyAsync(answer, new() { Delay = Random.Shared.Next(25, 70) });
                    break;
            }
            result.Add(new FillFieldDto(FieldMatcher.Clean(field.Label), answer, field.Required, "answer"));
        }
        return result;
    }

    /// <summary>Tags each visible control with data-jh-index and returns its accessible label.</summary>
    private static async Task<IReadOnlyList<PageField>> ScanAsync(IPage page)
    {
        const string script = """
            () => {
              const out = [];
              const controls = Array.from(document.querySelectorAll('input, textarea, select'))
                .filter(e => !['hidden','submit','button','image','reset','password'].includes((e.type||'').toLowerCase()))
                .filter(e => e.offsetParent !== null || e.type === 'file')
                .filter(e => !e.disabled && !e.hasAttribute('data-jh-done'));
              controls.forEach((e, i) => {
                const index = Date.now() % 100000 * 100 + i;
                e.setAttribute('data-jh-index', String(index));
                e.setAttribute('data-jh-done', '1');
                let label = e.getAttribute('aria-label') || '';
                if (!label && e.id) { const l = document.querySelector(`label[for="${CSS.escape(e.id)}"]`); if (l) label = l.innerText; }
                if (!label) { const l = e.closest('label'); if (l) label = l.innerText; }
                if (!label) label = e.getAttribute('placeholder') || e.name || '';
                const kind = e.tagName === 'SELECT' ? 'select' : e.tagName === 'TEXTAREA' ? 'textarea' : (e.type || 'text').toLowerCase();
                const options = e.tagName === 'SELECT' ? Array.from(e.options).map(o => o.text).filter(t => t.trim()) : [];
                out.push({ index, label: label.trim(), kind, required: e.required || e.getAttribute('aria-required') === 'true' || /\*/.test(label), options });
              });
              return out;
            }
            """;
        var raw = await page.EvaluateAsync<JsonElement>(script);
        return raw.EnumerateArray().Select(e => new PageField(
            e.GetProperty("index").GetInt32(),
            e.GetProperty("label").GetString() ?? "",
            e.GetProperty("kind").GetString() ?? "text",
            e.GetProperty("required").GetBoolean(),
            e.GetProperty("options").EnumerateArray().Select(o => o.GetString() ?? "").ToList()))
            .Where(f => f.Label.Length > 0)
            .ToList();
    }

    private static async Task<ILocator?> FindButtonAsync(IPage page, string pattern)
    {
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var role in new[] { AriaRole.Button, AriaRole.Link })
        {
            var locator = page.GetByRole(role, new() { NameRegex = regex });
            if (await locator.CountAsync() > 0 && await locator.First.IsVisibleAsync()) return locator.First;
        }
        var input = page.Locator("input[type=submit]").Filter(new() { Has = null });
        for (var i = 0; i < await input.CountAsync(); i++)
            if (regex.IsMatch(await input.Nth(i).GetAttributeAsync("value") ?? "")) return input.Nth(i);
        return null;
    }

    private async Task<bool> ClickIfVisibleAsync(IPage page, string pattern)
    {
        var button = await FindButtonAsync(page, pattern);
        if (button is null) return false;
        await pause(500, 1500);
        await button.ClickAsync();
        try { await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 10000 }); } catch (TimeoutException) { }
        await pause(600, 1400);
        return true;
    }
}
