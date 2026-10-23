using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.Logging;
using ArvindJobHunter.ResumeAutomation.Word;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Worker.Browser;

// Local document-automation worker.
// - Binds to loopback only (never reachable from the network).
// - Every request must carry the shared secret (X-Worker-Secret).
// - The master resume is opened read-only; outputs are always copies.
// - /resume/apply is idempotent per idempotencyKey.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddMarkdownFile(options =>
{
    options.Directory = Path.Combine(builder.Environment.ContentRootPath, "logs");
    options.FileNamePrefix = "jobhunter-worker";
    options.Title = "Job Hunter Worker";
});

var secret = builder.Configuration["Worker:SharedSecret"];
var generatedSecret = false;
if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
{
    secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    generatedSecret = true;
    // Console only: the secret must never be written to a log file.
    Console.WriteLine($"Worker:SharedSecret not configured; generated one-time secret: {secret}");
}

var outputRoot = Path.GetFullPath(builder.Configuration["Worker:OutputDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "output"));
Directory.CreateDirectory(outputRoot);
var port = builder.Configuration.GetValue<int?>("Worker:Port") ?? 5310;

builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port));
builder.Services.AddSingleton<IResumeDocumentService, OpenXmlResumeDocumentService>();
builder.Services.AddSingleton<IDocumentExporter>(sp => new OfficeDocumentExporter(sp.GetRequiredService<ILogger<OfficeDocumentExporter>>()));
builder.Services.AddSingleton<ConcurrentDictionary<string, ResumeChangeReport>>();
var browserRoot = Path.GetFullPath(builder.Configuration["Worker:BrowserDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "browser"));
var browserOptions = new BrowserOptions(
    Path.Combine(browserRoot, "profile"),
    Path.Combine(browserRoot, "screenshots"),
    builder.Configuration.GetValue<bool?>("Worker:Headless") ?? false,
    builder.Configuration["Worker:DownloadsDirectory"] ?? Path.Combine(browserRoot, "downloads"));
builder.Services.AddSingleton(browserOptions);
builder.Services.AddSingleton(_ => AdapterRegistry.FromConfig(Path.Combine(AppContext.BaseDirectory, "Browser", "sites.json")));
builder.Services.AddSingleton<BrowserSessionManager>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();
app.LogUnhandledExceptions("ArvindJobHunter.Worker");
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ArvindJobHunter.Worker");
log.LogInformation("Resume worker listening on 127.0.0.1:{Port}; output directory {OutputRoot}; Markdown logs in {LogDirectory}",
    port, outputRoot, app.Services.GetMarkdownLogger()?.LogDirectory ?? "(disabled)");
if (generatedSecret)
{
    log.LogWarning("Worker:SharedSecret is not configured; a one-time secret was generated and printed to the console only. Configure the same Worker:SharedSecret for the worker and the API");
}

app.UseMarkdownRequestLogging();
app.Use(async (ctx, next) =>
{
    if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.None))
    {
        log.LogWarning("Rejected {Method} {Path} from non-loopback address {RemoteIp}", ctx.Request.Method, ctx.Request.Path, ctx.Connection.RemoteIpAddress);
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    if (ctx.Request.Path != "/health")
    {
        var provided = ctx.Request.Headers["X-Worker-Secret"].ToString();
        var ok = provided.Length == secret.Length &&
                 CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(secret));
        if (!ok)
        {
            log.LogWarning("Rejected {Method} {Path}: missing or wrong X-Worker-Secret", ctx.Request.Method, ctx.Request.Path);
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "worker", outputRoot }));

app.MapPost("/resume/read", async (ReadResumeRequest request, IResumeDocumentService documents, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MasterPath)) return Results.BadRequest(new { error = "masterPath is required." });
    try { return Results.Ok(await documents.ReadAsync(request.MasterPath, ct)); }
    catch (FileNotFoundException ex)
    {
        log.LogWarning("Resume read failed: {Error} ({Path})", ex.Message, request.MasterPath);
        return Results.NotFound(new { error = ex.Message });
    }
});

app.MapPost("/resume/apply", async (ApplyResumeRequest request, IResumeDocumentService documents, ConcurrentDictionary<string, ResumeChangeReport> receipts, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return Results.BadRequest(new { error = "idempotencyKey is required." });
    if (string.IsNullOrWhiteSpace(request.MasterPath)) return Results.BadRequest(new { error = "masterPath is required." });
    if (request.Changes is null || request.Changes.Count == 0) return Results.BadRequest(new { error = "At least one change is required." });

    if (receipts.TryGetValue(request.IdempotencyKey, out var existing))
    {
        log.LogInformation("Resume apply {IdempotencyKey} replayed; returning the earlier result ({Output})", request.IdempotencyKey, existing.OutputPath);
        return Results.Ok(existing);
    }

    var safeName = string.Concat((request.OutputFileName ?? $"resume-{request.IdempotencyKey}.docx").Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
    if (!safeName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) safeName += ".docx";
    var outputPath = Path.GetFullPath(Path.Combine(outputRoot, safeName));
    if (!outputPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
    {
        log.LogWarning("Resume apply {IdempotencyKey} rejected: output {Output} escapes the output directory", request.IdempotencyKey, outputPath);
        return Results.BadRequest(new { error = "Output must stay inside the worker output directory." });
    }

    try
    {
        var report = await documents.ApplyChangesAsync(request.MasterPath, outputPath, request.Changes, ct);
        receipts.TryAdd(request.IdempotencyKey, report);
        return Results.Ok(report);
    }
    catch (FileNotFoundException ex)
    {
        log.LogWarning("Resume apply {IdempotencyKey} failed: {Error} ({Path})", request.IdempotencyKey, ex.Message, request.MasterPath);
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        log.LogWarning(ex, "Resume apply {IdempotencyKey} rejected", request.IdempotencyKey);
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/resume/export-pdf", async (ExportPdfRequest request, IDocumentExporter exporter, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.DocxPath)) return Results.BadRequest(new { error = "docxPath is required." });
    var source = Path.GetFullPath(request.DocxPath);
    if (!source.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase) || !source.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
    {
        log.LogWarning("PDF export rejected: {Path} is not a .docx inside the worker output directory", source);
        return Results.BadRequest(new { error = "Only .docx files inside the worker output directory can be exported." });
    }
    if (exporter.AvailableEngine is null) return Results.Problem("PDF export needs Microsoft Word or LibreOffice installed.", statusCode: StatusCodes.Status501NotImplemented);

    try { return Results.Ok(await exporter.ExportPdfAsync(source, Path.ChangeExtension(source, ".pdf"), ct)); }
    catch (FileNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (InvalidOperationException ex)
    {
        log.LogWarning(ex, "PDF export failed for {Path}", source);
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapPost("/apply/prepare", async (FillPreviewRequest request, BrowserSessionManager browser, CancellationToken ct) =>
{
    try { return Results.Ok(await browser.PrepareAsync(request, ct)); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (FileNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
    catch (Microsoft.Playwright.PlaywrightException ex)
    {
        log.LogWarning(ex, "Browser prepare failed for {Url}", request.Url);
        return Results.Problem($"Browser automation failed: {ex.Message}. Run 'pwsh bin/.../playwright.ps1 install chromium' once if the browser is missing.", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/apply/bind", (BindSessionRequest request, BrowserSessionManager browser) =>
    browser.Bind(request.SessionId, request.PayloadHash) ? Results.Ok() : Results.NotFound(new { error = "Session not found." }));

app.MapPost("/apply/submit", async (SubmitRequest request, BrowserSessionManager browser, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return Results.BadRequest(new { error = "idempotencyKey is required." });
    return Results.Ok(await browser.SubmitAsync(request, ct));
});

app.MapDelete("/apply/{sessionId}", async (string sessionId, BrowserSessionManager browser) => { await browser.DiscardAsync(sessionId); return Results.NoContent(); });

app.MapPost("/browser/open", async (OpenBrowserRequest request, BrowserSessionManager browser) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return Results.BadRequest(new { error = "A http(s) URL is required." });
    await browser.OpenAsync(request.Url);
    return Results.Ok(new { opened = request.Url, downloads = browserOptions.DownloadsDirectory });
});

app.MapGet("/browser/downloads", () =>
{
    var dir = browserOptions.DownloadsDirectory!;
    Directory.CreateDirectory(dir);
    return Results.Ok(new DirectoryInfo(dir).GetFiles("*.pdf").OrderByDescending(f => f.LastWriteTimeUtc)
        .Select(f => new { path = f.FullName, name = f.Name, size = f.Length, modified = f.LastWriteTimeUtc }));
});

app.MapPost("/browser/capture", async (BrowserSessionManager browser) =>
    await browser.CaptureActiveAsync() is { } captured ? Results.Ok(captured) : Results.NotFound(new { error = "No web page is open in the browser." }));

app.MapPost("/browser/links", async (BrowserSessionManager browser) =>
    await browser.LinksActiveAsync() is { } links ? Results.Ok(links) : Results.NotFound(new { error = "No web page is open in the browser." }));

app.Run();

public sealed record PageLinkResult(string Text, string Url);

public sealed record PageLinksResult(string PageUrl, IReadOnlyList<PageLinkResult> Links);

public sealed record CapturedPage(string Url, string Title, string PageTitle, string Text);

public sealed record BindSessionRequest(string SessionId, string PayloadHash);

public sealed record OpenBrowserRequest(string Url);

public sealed record ExportPdfRequest(string DocxPath);

public sealed record ReadResumeRequest(string MasterPath);

public sealed record ApplyResumeRequest(string IdempotencyKey, string MasterPath, string? OutputFileName, IReadOnlyList<ResumeChange> Changes);

public partial class Program;
