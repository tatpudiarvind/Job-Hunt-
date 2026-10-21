using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Api.Endpoints;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapSubmissionEndpoints(this IEndpointRouteBuilder endpoints, string dataDirectory)
    {
        var group = endpoints.MapGroup("/api/submissions").WithTags("Submissions").RequireAuthorization();

        group.MapGet("", (Guid? jobId, ApplicationSubmissionService service, CancellationToken ct) => service.ListAsync(jobId, ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ApplicationSubmissionService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } package ? Results.Ok(package) : Results.NotFound());

        group.MapPost("/jobs/{jobId:guid}/prepare", async Task<IResult> (Guid jobId, PrepareSubmissionRequest? request, ClaimsPrincipal user, ApplicationSubmissionService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.PrepareAsync(jobId, request?.Adapter, user.UserId(), ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Could not prepare the application");
            }
        });

        group.MapPost("/{id:guid}/answers", async Task<IResult> (Guid id, AnswerSubmissionRequest request, ClaimsPrincipal user, ApplicationSubmissionService service, CancellationToken ct) =>
        {
            try
            {
                return await service.AnswerAsync(id, request.Answers, user.UserId(), ct) is { } package ? Results.Ok(package) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Could not update answers");
            }
        });

        group.MapGet("/{id:guid}/screenshot", async Task<IResult> (Guid id, ApplicationSubmissionService service, CancellationToken ct) =>
        {
            var package = await service.GetAsync(id, ct);
            return package?.ScreenshotPath is { } path && File.Exists(path) ? Results.File(path, "image/png") : Results.NotFound();
        });

        var sites = endpoints.MapGroup("/api/sites").WithTags("Sites").RequireAuthorization();

        sites.MapGet("", async (IRuntimeSettingsProvider settings, IRepository<SubmissionPackage> packages, CancellationToken ct) =>
        {
            var current = await settings.GetAsync(ct);
            var all = await packages.ListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            return Effective(current).Select(s => new SiteSettingsDto(s.Adapter, s.Enabled, s.DailyCap, SiteSettings.SubmittedToday(all, s.Adapter, now))).ToList();
        });

        sites.MapPut("", async Task<IResult> (UpdateSiteSettingsRequest request, IRuntimeSettingsProvider settings, CancellationToken ct) =>
        {
            if (request.Sites.Any(s => string.IsNullOrWhiteSpace(s.Adapter) || s.DailyCap is < 0 or > 100))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["sites"] = ["Each site needs an adapter and a daily cap between 0 and 100."] });
            }

            var current = await settings.GetAsync(ct);
            var updated = request.Sites.Select(s => new SiteSettings(s.Adapter.Trim().ToLowerInvariant(), s.Enabled, s.DailyCap)).ToList();
            await settings.SaveAsync(current with { Sites = updated }, ct);
            return Results.Ok(updated.Select(s => new SiteSettingsDto(s.Adapter, s.Enabled, s.DailyCap)).ToList());
        });

        var browser = endpoints.MapGroup("/api/browser").WithTags("Browser").RequireAuthorization();

        // Opens a site (e.g. ResumeNow) in the visible browser. The user edits and downloads there; nothing is automated.
        browser.MapPost("/open", async Task<IResult> (OpenUrlRequest request, [FromServices] IBrowserApplyClient? client, CancellationToken ct) =>
        {
            if (client is null) return Results.Problem("The Worker browser is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["A http(s) URL is required."] });
            try { await client.OpenAsync(uri.ToString(), ct); return Results.Ok(); }
            catch (HttpRequestException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway); }
        });

        browser.MapGet("/downloads", async Task<IResult> ([FromServices] IBrowserApplyClient? client, CancellationToken ct) =>
            client is null ? Results.Ok(Array.Empty<DownloadedFile>()) : Results.Ok(await client.DownloadsAsync(ct)));

        // Copies a downloaded PDF into the resume library. The user picks the file explicitly; only Worker download paths are accepted.
        browser.MapPost("/downloads/import", async Task<IResult> (ImportDownloadRequest request, ClaimsPrincipal user, [FromServices] IBrowserApplyClient? client, IRuntimeSettingsProvider settings, AuditService audit, CancellationToken ct) =>
        {
            if (client is null) return Results.Problem("The Worker browser is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var file = (await client.DownloadsAsync(ct)).FirstOrDefault(f => string.Equals(f.Path, request.Path, StringComparison.OrdinalIgnoreCase));
            if (file is null || !File.Exists(file.Path)) return Results.NotFound();

            var directory = Path.Combine(dataDirectory, "resumes", "library");
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Path.GetFileName(file.Name)}");
            File.Copy(file.Path, target);

            var current = await settings.GetAsync(ct);
            var keywords = (request.Keywords ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
            var entry = new ResumeLibraryEntry(target, string.IsNullOrWhiteSpace(request.Label) ? Path.GetFileNameWithoutExtension(file.Name) : request.Label.Trim(), keywords);
            await settings.SaveAsync(current with { ResumeLibrary = [.. current.ResumeLibrary, entry] }, ct);
            await audit.RecordAsync(user.UserId(), "RESUME_IMPORTED", "ResumeLibrary", target, "SUCCESS", entry.Label, ct);
            return Results.Ok(entry);
        });

        // Turns the page open in the visible browser into a job draft for review. Nothing is saved until the user creates the job.
        browser.MapPost("/capture-job", async Task<IResult> ([FromServices] IBrowserApplyClient? client, CancellationToken ct) =>
        {
            if (client is null) return Results.Problem("The Worker browser is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
            var page = await client.CaptureAsync(ct);
            if (page is null) return Results.NotFound();
            var uri = new Uri(page.Url);
            var company = page.PageTitle.Split([" | ", " - ", " at "], StringSplitOptions.TrimEntries).Skip(1).FirstOrDefault();
            return Results.Ok(new JobPostingDraft(page.Url, page.Title, company, null, page.Text, uri.Host,
                company is null ? ["Company could not be read from the page; please fill it in."] : []));
        });

        browser.MapGet("/resume-library", async (IRuntimeSettingsProvider settings, CancellationToken ct) => (await settings.GetAsync(ct)).ResumeLibrary);

        return endpoints;
    }

    public sealed record OpenUrlRequest(string Url);
    public sealed record ImportDownloadRequest(string Path, string? Label, IReadOnlyList<string>? Keywords);

    private static IReadOnlyList<SiteSettings> Effective(RuntimeSettings settings) => settings.Sites.Count > 0 ? settings.Sites : SiteSettings.Defaults();
}
