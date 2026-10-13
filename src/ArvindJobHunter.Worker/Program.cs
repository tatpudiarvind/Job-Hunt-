using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.Logging;
using ArvindJobHunter.ResumeAutomation.Word;

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
builder.Services.AddSingleton<ConcurrentDictionary<string, ResumeChangeReport>>();
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

app.Run();

public sealed record ReadResumeRequest(string MasterPath);

public sealed record ApplyResumeRequest(string IdempotencyKey, string MasterPath, string? OutputFileName, IReadOnlyList<ResumeChange> Changes);

public partial class Program;
