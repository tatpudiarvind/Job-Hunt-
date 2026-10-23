using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArvindJobHunter.Contracts.Api;
using Microsoft.Playwright;

namespace ArvindJobHunter.Worker.Browser;

public sealed record BrowserOptions(string ProfileDirectory, string ScreenshotDirectory, bool Headless = false, string? DownloadsDirectory = null);

/// <summary>
/// One visible, persistent Chromium profile (the user's own logins and MFA stay in it). Each prepared submission
/// keeps its page open until it is submitted or discarded, so the approved form is the one that gets submitted.
/// </summary>
public sealed class BrowserSessionManager(BrowserOptions options, AdapterRegistry registry, ILogger<BrowserSessionManager> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (IPage Page, IJobSiteAdapter Adapter, string PayloadHash)> sessions = new();
    private readonly ConcurrentDictionary<string, SubmitResponse> submitted = new();
    private IPlaywright? playwright;
    private IBrowserContext? context;

    public async Task<IBrowserContext> ContextAsync()
    {
        if (context is not null) return context;
        await gate.WaitAsync();
        try
        {
            if (context is not null) return context;
            Directory.CreateDirectory(options.ProfileDirectory);
            playwright = await Playwright.CreateAsync();
            context = await playwright.Chromium.LaunchPersistentContextAsync(options.ProfileDirectory, new()
            {
                Headless = options.Headless,
                AcceptDownloads = true,
                DownloadsPath = options.DownloadsDirectory,
                ViewportSize = ViewportSize.NoViewport,
                Args = ["--start-maximized"]
            });
            logger.LogInformation("Browser started with persistent profile {Profile} (headless: {Headless})", options.ProfileDirectory, options.Headless);
            return context;
        }
        finally { gate.Release(); }
    }

    public async Task<IPage> OpenAsync(string url)
    {
        var ctx = await ContextAsync();
        var page = await ctx.NewPageAsync();
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 45000 });
        return page;
    }

    /// <summary>Reads the job page the user is looking at in the visible browser. One page, on request; never crawls.</summary>
    public async Task<CapturedPage?> CaptureActiveAsync()
    {
        var ctx = await ContextAsync();
        var page = ctx.Pages.LastOrDefault(p => p.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (page is null) return null;
        var title = await page.TitleAsync();
        var heading = await page.Locator("h1").First.TextContentAsync(new() { Timeout = 2000 }).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null);
        var text = await page.Locator("body").InnerTextAsync();
        return new CapturedPage(page.Url, heading?.Trim() ?? title, title, text.Length > 20000 ? text[..20000] : text);
    }

    /// <summary>Lists job-posting links on the page the user is looking at. Reads that one page; never opens the links.</summary>
    public async Task<PageLinksResult?> LinksActiveAsync()
    {
        var ctx = await ContextAsync();
        var page = ctx.Pages.LastOrDefault(p => p.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase));
        if (page is null) return null;
        var raw = await page.EvaluateAsync<RawLink[]>("() => Array.from(document.querySelectorAll('a[href]')).map(a => ({ text: a.innerText || a.getAttribute('aria-label') || '', href: a.getAttribute('href') }))");
        var links = JobLinkFilter.Filter(page.Url, raw).Select(l => new PageLinkResult(l.Text, l.Url)).ToList();
        return new PageLinksResult(page.Url, links);
    }

    public async Task<FillPreviewResponse> PrepareAsync(FillPreviewRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A http(s) application URL is required.");
        if (!File.Exists(request.ResumeFilePath)) throw new FileNotFoundException("Resume file not found.", request.ResumeFilePath);

        var adapter = registry.Resolve(uri, request.Adapter);
        var page = await OpenAsync(request.Url);
        var fill = await adapter.FillAsync(page, request, cancellationToken);
        var sessionId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(options.ScreenshotDirectory);
        var screenshot = Path.Combine(options.ScreenshotDirectory, $"{sessionId}.png");
        await page.ScreenshotAsync(new() { Path = screenshot, FullPage = true });
        sessions[sessionId] = (page, adapter, "");
        logger.LogInformation("Prepared {Adapter} form for {Host}: {Filled} field(s), {Missing} unanswered, blocker: {Blocker}",
            adapter.Name, uri.Host, fill.Fields.Count, fill.Unanswered.Count, fill.Blocker ?? "none");
        return new FillPreviewResponse(sessionId, adapter.Name, fill.Fields, fill.Unanswered, screenshot, fill.Blocker);
    }

    /// <summary>Records the hash of the package the API will ask the user to approve; submit must present the same hash.</summary>
    public bool Bind(string sessionId, string payloadHash)
    {
        if (!sessions.TryGetValue(sessionId, out var session)) return false;
        sessions[sessionId] = session with { PayloadHash = payloadHash };
        return true;
    }

    public async Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken cancellationToken)
    {
        if (submitted.TryGetValue(request.IdempotencyKey, out var replay)) return replay;
        if (!sessions.TryGetValue(request.SessionId, out var session))
            return new SubmitResponse(false, null, null, "The prepared browser page is gone (worker restarted or closed). Prepare the submission again.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(session.PayloadHash), Encoding.UTF8.GetBytes(request.ExpectedPayloadHash)))
            return new SubmitResponse(false, null, null, "The approved package does not match the prepared form. Prepare again.");

        var result = await session.Adapter.SubmitAsync(session.Page, cancellationToken);
        var screenshot = Path.Combine(options.ScreenshotDirectory, $"{request.SessionId}-after.png");
        try { await session.Page.ScreenshotAsync(new() { Path = screenshot, FullPage = true }); } catch (PlaywrightException) { screenshot = null!; }
        result = result with { ScreenshotPath = screenshot };
        submitted[request.IdempotencyKey] = result;
        if (result.Submitted) sessions.TryRemove(request.SessionId, out _);
        return result;
    }

    public async Task DiscardAsync(string sessionId)
    {
        if (sessions.TryRemove(sessionId, out var session)) await session.Page.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (context is not null) await context.DisposeAsync();
        playwright?.Dispose();
    }
}
