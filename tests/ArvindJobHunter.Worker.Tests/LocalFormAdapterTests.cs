using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Worker.Browser;
using Microsoft.Playwright;

namespace ArvindJobHunter.Worker.Tests;

/// <summary>
/// Drives the configured adapter against local HTML forms in headless Chromium. Skips when the
/// Playwright browser isn't installed, so CI without browsers stays green.
/// </summary>
public sealed class LocalFormAdapterTests : IAsyncLifetime
{
    private static readonly SiteConfig Company = new("company", ["*"], "Apply", "Next|Continue", "Submit( application)?", "thank you for applying|application (has been )?(received|submitted)", "Sign in");
    private IPlaywright? playwright;
    private IBrowser? browser;
    private string? skipReason;

    public async Task InitializeAsync()
    {
        try
        {
            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (PlaywrightException ex) { skipReason = ex.Message; }
    }

    public async Task DisposeAsync()
    {
        if (browser is not null) await browser.DisposeAsync();
        playwright?.Dispose();
    }

    private const string FormHtml = """
        <form onsubmit="event.preventDefault(); document.body.innerHTML = '<p>Thank you for applying!</p>';">
          <label for="fn">First name *</label><input id="fn" required>
          <label for="em">Email *</label><input id="em" type="email" required>
          <label for="sp">Do you require sponsorship? *</label><select id="sp" required><option value=""></option><option>Yes</option><option>No</option></select>
          <label for="why">Why do you want this job? *</label><textarea id="why" required></textarea>
          <label for="cv">Resume</label><input id="cv" type="file">
          <button type="submit">Submit application</button>
        </form>
        """;

    private async Task<(IPage Page, string Resume)> OpenAsync(string html)
    {
        var page = await browser!.NewPageAsync();
        await page.SetContentAsync(html);
        var resume = Path.Combine(Path.GetTempPath(), $"cv-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(resume, "%PDF-1.4");
        return (page, resume);
    }

    private static ConfiguredFormAdapter Adapter() => new(Company, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Fill_FillsKnownFields_ReportsUnanswered_AndDoesNotSubmit()
    {
        if (skipReason is not null) return;
        var (page, resume) = await OpenAsync(FormHtml);
        var answers = new Dictionary<string, string> { ["First name"] = "Arvind", ["Email"] = "a@b.com", ["Sponsorship"] = "No" };

        var result = await Adapter().FillAsync(page, new FillPreviewRequest("about:blank", resume, null, answers, "company"), CancellationToken.None);

        Assert.Null(result.Blocker);
        Assert.Equal("Arvind", await page.InputValueAsync("#fn"));
        Assert.Equal("No", await page.InputValueAsync("#sp"));
        Assert.Contains(result.Unanswered, u => u.StartsWith("Why do you want"));
        Assert.DoesNotContain("Thank you", await page.InnerTextAsync("body"));
    }

    [Fact]
    public async Task Fill_UploadsCoverLetterFileOnlyWhenProvided()
    {
        if (skipReason is not null) return;
        const string html = """
            <form><label for="cv">Resume</label><input id="cv" type="file">
            <label for="cl">Cover letter</label><input id="cl" type="file"><button type="submit">Submit application</button></form>
            """;
        var (page, resume) = await OpenAsync(html);
        var cover = Path.Combine(Path.GetTempPath(), $"cl-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(cover, "%PDF-1.4");

        var without = await Adapter().FillAsync(page, new FillPreviewRequest("about:blank", resume, null, new Dictionary<string, string>(), "company"), CancellationToken.None);
        Assert.Contains(without.Fields, f => f.Source == "skipped");
        Assert.Equal(0, await page.EvalOnSelectorAsync<int>("#cl", "e => e.files.length"));

        var (fresh, _) = await OpenAsync(html);
        var with = await Adapter().FillAsync(fresh, new FillPreviewRequest("about:blank", resume, null, new Dictionary<string, string>(), "company", cover), CancellationToken.None);
        Assert.Contains(with.Fields, f => f.Source == "cover letter" && f.Value == Path.GetFileName(cover));
        Assert.Equal(1, await fresh.EvalOnSelectorAsync<int>("#cl", "e => e.files.length"));
    }

    [Fact]
    public async Task Submit_ClicksFinalButtonAndReadsConfirmation()
    {
        if (skipReason is not null) return;
        var (page, resume) = await OpenAsync(FormHtml);
        var answers = new Dictionary<string, string> { ["First name"] = "Arvind", ["Email"] = "a@b.com", ["Sponsorship"] = "No", ["Why do you want this job?"] = "Mission" };
        var adapter = Adapter();
        await adapter.FillAsync(page, new FillPreviewRequest("about:blank", resume, null, answers, "company"), CancellationToken.None);

        var submitted = await adapter.SubmitAsync(page, CancellationToken.None);

        Assert.True(submitted.Submitted, submitted.Error);
    }

    [Fact]
    public async Task Verify_ReportsCaptchaAndLogin()
    {
        if (skipReason is not null) return;
        var (captcha, _) = await OpenAsync("<div class='g-recaptcha'></div>");
        Assert.Contains("CAPTCHA", await Adapter().VerifyAsync(captcha));
        var (login, _) = await OpenAsync("<form><input type='password'></form>");
        Assert.Contains("sign in", await Adapter().VerifyAsync(login));
    }
}
