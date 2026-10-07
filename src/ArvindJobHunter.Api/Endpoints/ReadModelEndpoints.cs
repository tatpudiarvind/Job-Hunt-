using System.Security.Claims;
using ArvindJobHunter.Agents.Llm;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Infrastructure.Persistence;

namespace ArvindJobHunter.Api.Endpoints;

public static class ReadModelEndpoints
{
    public static IEndpointRouteBuilder MapReadModelEndpoints(this IEndpointRouteBuilder endpoints, string dataDirectory)
    {
        var api = endpoints.MapGroup("/api").RequireAuthorization();

        api.MapGet("/agent-runs", (AgentRunService service, CancellationToken ct) => service.ListAsync(ct)).WithTags("Agent Runs");
        api.MapGet("/agent-runs/{id:guid}", async Task<IResult> (Guid id, AgentRunService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } run ? Results.Ok(run) : Results.NotFound()).WithTags("Agent Runs");

        api.MapGet("/audit-logs", (AuditService service, CancellationToken ct) => service.ListAsync(ct)).WithTags("Audit");

        api.MapGet("/dashboard", async (JobService jobs, ApplicationService applications, ApprovalService approvals, AgentRunService runs,
            ExecuteApprovedActionCommand executions, CandidateFactService facts, IRuntimeSettingsProvider settings, ILlmProviderResolver llm, CancellationToken ct) =>
        {
            var jobList = await jobs.ListAsync(ct);
            var approvalList = await approvals.ListAsync(ct);
            var current = await settings.GetAsync(ct);
            return new DashboardResponse(
                jobList.Count,
                jobList.Count(j => j.Status == JobStatus.QUALIFIED),
                (await applications.ListAsync(ct)).Count,
                approvalList.Count(a => a.Status == ApprovalStatus.PENDING),
                (await runs.ListAsync(ct)).Count,
                (await executions.ListAsync(ct)).Count(r => r.Mode == ExecutionMode.LIVE),
                (await facts.ListVerifiedAsync(ct)).Count,
                current.Mode.ToString(),
                (await llm.ResolveAsync(ct)).Name);
        }).WithTags("Dashboard");

        api.MapGet("/settings", async (IRuntimeSettingsProvider settings, OpenAiOptions openAi, CancellationToken ct) =>
        {
            var current = await settings.GetAsync(ct);
            return new SettingsResponse(current.Mode.ToString(), current.LlmProvider, !string.IsNullOrWhiteSpace(openAi.ApiKey),
                current.MasterResumePath, !string.IsNullOrWhiteSpace(current.MasterResumePath) && File.Exists(current.MasterResumePath), dataDirectory);
        }).WithTags("Settings");

        api.MapPut("/settings", async Task<IResult> (UpdateSettingsRequest request, ClaimsPrincipal user, IRuntimeSettingsProvider settings, AuditService audit, CancellationToken ct) =>
        {
            var current = await settings.GetAsync(ct);
            var mode = current.Mode;
            if (request.Mode is not null && !Enum.TryParse(request.Mode, true, out mode))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Mode must be DEMO, DRY_RUN, or LIVE."] });
            }

            var updated = new RuntimeSettings
            {
                Mode = mode,
                LlmProvider = request.LlmProvider ?? current.LlmProvider,
                MasterResumePath = request.MasterResumePath ?? current.MasterResumePath,
                QualificationThreshold = current.QualificationThreshold,
                ApprovalTtlHours = current.ApprovalTtlHours
            };
            await settings.SaveAsync(updated, ct);
            await audit.RecordAsync(user.UserId(), "SETTINGS_UPDATED", "RuntimeSettings", "local", "SUCCESS", $"Mode={updated.Mode}; Llm={updated.LlmProvider}", ct);
            return Results.Ok(updated);
        }).WithTags("Settings");

        api.MapPost("/settings/master-resume", async Task<IResult> (HttpRequest request, ClaimsPrincipal user, IRuntimeSettingsProvider settings, AuditService audit, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count != 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload exactly one .docx or .pdf file."] });
            }

            var file = request.Form.Files[0];
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".docx" or ".pdf"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Master resume must be a .docx (Word) or .pdf document."] });
            }
            if (file.Length is 0 or > 20 * 1024 * 1024)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["File must be between 1 byte and 20 MB."] });
            }

            var resumeDirectory = Path.Combine(dataDirectory, "resumes");
            Directory.CreateDirectory(resumeDirectory);
            var safeName = string.Concat(Path.GetFileNameWithoutExtension(file.FileName).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var targetPath = Path.Combine(resumeDirectory, $"master-{safeName}{extension}");
            await using (var target = File.Create(targetPath))
            await using (var source = file.OpenReadStream())
            {
                await source.CopyToAsync(target, ct);
            }

            var current = await settings.GetAsync(ct);
            var updated = new RuntimeSettings
            {
                Mode = current.Mode,
                LlmProvider = current.LlmProvider,
                MasterResumePath = targetPath,
                QualificationThreshold = current.QualificationThreshold,
                ApprovalTtlHours = current.ApprovalTtlHours
            };
            await settings.SaveAsync(updated, ct);
            await audit.RecordAsync(user.UserId(), "MASTER_RESUME_UPLOADED", "RuntimeSettings", "local", "SUCCESS", $"{file.FileName} ({file.Length} bytes) -> {targetPath}", ct);
            return Results.Ok(new { masterResumePath = targetPath, fileName = file.FileName, size = file.Length });
        }).WithTags("Settings").DisableAntiforgery();

        api.MapGet("/data/export", async (ClaimsPrincipal user, DataPortabilityService portability, AuditService audit, CancellationToken ct) =>
        {
            var bytes = await portability.ExportAsync(ct);
            await audit.RecordAsync(user.UserId(), "DATA_EXPORTED", "DataDirectory", "local", "SUCCESS", $"{bytes.Length} bytes", ct);
            return Results.File(bytes, "application/zip", $"arvind-job-hunter-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
        }).WithTags("Data");

        api.MapPost("/data/import", async Task<IResult> (HttpRequest request, ClaimsPrincipal user, DataPortabilityService portability, IEnumerable<ICacheInvalidatable> caches, AuditService audit, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count != 1)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["Upload exactly one .zip export file."] });
            }

            try
            {
                await using var stream = request.Form.Files[0].OpenReadStream();
                var result = await portability.ImportAsync(stream, ct);
                foreach (var cache in caches) cache.Invalidate();
                await audit.RecordAsync(user.UserId(), "DATA_IMPORTED", "DataDirectory", "local", "SUCCESS", $"Files: {string.Join(", ", result.ImportedFiles)}; backup: {result.BackupDirectory}", ct);
                return Results.Ok(result);
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).WithTags("Data").DisableAntiforgery();

        var google = endpoints.MapGroup("/api/integrations/google").WithTags("Integrations");
        google.MapGet("/status", (IGoogleOAuthService service, CancellationToken ct) => service.GetStatusAsync(ct)).RequireAuthorization();
        google.MapGet("/authorize", async Task<IResult> (ClaimsPrincipal user, IGoogleOAuthService service, CancellationToken ct) =>
        {
            try { return Results.Ok(new { url = (await service.CreateAuthorizationUriAsync(user.UserId(), ct)).ToString() }); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status412PreconditionFailed); }
        }).RequireAuthorization();
        google.MapGet("/callback", async Task<IResult> (string code, string state, IGoogleOAuthService service, IConfiguration configuration, CancellationToken ct) =>
            await service.CompleteAsync(code, state, ct)
                ? Results.Redirect((configuration["Web:BaseUrl"] ?? "http://localhost:4200") + "/settings?google=connected")
                : Results.BadRequest("Google authorization failed.")).AllowAnonymous();
        google.MapPost("/revoke", async (IGoogleOAuthService service, CancellationToken ct) => { await service.RevokeAsync(ct); return Results.NoContent(); }).RequireAuthorization();

        return endpoints;
    }
}
