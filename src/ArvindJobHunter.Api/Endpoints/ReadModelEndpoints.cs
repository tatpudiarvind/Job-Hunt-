using System.Security.Claims;
using ArvindJobHunter.Agents.Llm;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Infrastructure.Persistence;
using System.ComponentModel.DataAnnotations;

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
            ToSettingsResponse(await settings.GetAsync(ct), openAi, dataDirectory)).WithTags("Settings");

        api.MapPut("/settings", async Task<IResult> (UpdateSettingsRequest request, ClaimsPrincipal user, IRuntimeSettingsProvider settings, OpenAiOptions openAi, AuditService audit, CancellationToken ct) =>
        {
            var current = await settings.GetAsync(ct);
            var mode = current.Mode;
            if (request.Mode is not null && !Enum.TryParse(request.Mode, true, out mode))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mode"] = ["Mode must be DEMO, DRY_RUN, or LIVE."] });
            }

            var provider = (request.LlmProvider ?? current.LlmProvider).Trim();
            if (!string.Equals(provider, "Demo", StringComparison.OrdinalIgnoreCase) && !OpenAiLlmProvider.Handles(provider))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["llmProvider"] = ["LLM provider must be Demo, OpenAI-compatible, or Gemini."] });
            }

            var displayName = (request.LlmDisplayName ?? current.LlmDisplayName).Trim();
            var baseUrl = (request.LlmBaseUrl ?? current.LlmBaseUrl).Trim();
            var apiKey = request.LlmApiKey is null
                ? current.LlmApiKey
                : string.IsNullOrWhiteSpace(request.LlmApiKey) && !string.IsNullOrWhiteSpace(current.LlmApiKey)
                    ? current.LlmApiKey
                    : request.LlmApiKey.Trim();
            var model = (request.LlmModel ?? current.LlmModel).Trim();
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(displayName)) errors["llmDisplayName"] = ["Display name is required."];
            if (OpenAiLlmProvider.Handles(provider))
            {
                if (string.IsNullOrWhiteSpace(baseUrl)) errors["llmBaseUrl"] = ["Base URL is required for OpenAI-compatible providers."];
                else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) errors["llmBaseUrl"] = ["Base URL must be an absolute URL."];
                if (string.IsNullOrWhiteSpace(apiKey)) errors["llmApiKey"] = ["API key is required for OpenAI-compatible providers."];
                if (string.IsNullOrWhiteSpace(model)) errors["llmModel"] = ["Model is required for OpenAI-compatible providers."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var updated = new RuntimeSettings
            {
                Mode = mode,
                LlmProvider = provider,
                LlmDisplayName = displayName,
                LlmBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? current.LlmBaseUrl : baseUrl,
                LlmApiKey = apiKey,
                LlmModel = string.IsNullOrWhiteSpace(model) ? current.LlmModel : model,
                MasterResumePath = request.MasterResumePath ?? current.MasterResumePath,
                QualificationThreshold = current.QualificationThreshold,
                ApprovalTtlHours = current.ApprovalTtlHours
            };
            await settings.SaveAsync(updated, ct);
            await audit.RecordAsync(user.UserId(), "SETTINGS_UPDATED", "RuntimeSettings", "local", "SUCCESS", $"Mode={updated.Mode}; Llm={updated.LlmProvider}; DisplayName={updated.LlmDisplayName}; BaseUrl={updated.LlmBaseUrl}; Model={updated.LlmModel}", ct);
            // Same shape as GET: the API key is write-only and never echoed back to the browser.
            return Results.Ok(ToSettingsResponse(updated, openAi, dataDirectory));
        }).WithTags("Settings");

        api.MapGet("/settings/job-search-preferences", async (IJobSearchPreferencesProvider preferences, CancellationToken ct) =>
            ToJobSearchPreferencesResponse(await preferences.GetAsync(ct))).WithTags("Settings");

        api.MapPut("/settings/job-search-preferences", async Task<IResult> (UpdateJobSearchPreferencesRequest request, ClaimsPrincipal user, IJobSearchPreferencesProvider preferences, AuditService audit, CancellationToken ct) =>
        {
            var countries = CleanList(request.PreferredCountries);
            var roles = CleanList(request.PreferredRoles);
            var linkedInUrl = CleanOptional(request.LinkedInProfileUrl);
            var workMode = CleanOptional(request.WorkMode);
            var salaryExpectation = CleanOptional(request.SalaryExpectation);
            var workAuthorization = CleanOptional(request.WorkAuthorization);
            var errors = new Dictionary<string, string[]>();

            if (linkedInUrl is not null)
            {
                if (!Uri.TryCreate(linkedInUrl, UriKind.Absolute, out var linkedInUri)) errors["linkedInProfileUrl"] = ["LinkedIn profile URL must be an absolute URL."];
                else if (!(linkedInUri.Host.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) || linkedInUri.Host.Contains("lnkd.in", StringComparison.OrdinalIgnoreCase))) errors["linkedInProfileUrl"] = ["LinkedIn profile URL must point to LinkedIn."];
            }

            if (workMode is not null && !new[] { "Remote", "Hybrid", "Onsite", "Flexible" }.Contains(workMode, StringComparer.OrdinalIgnoreCase))
            {
                errors["workMode"] = ["Work mode must be Remote, Hybrid, Onsite, or Flexible."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var current = await preferences.GetAsync(ct);
            var updated = new JobSearchPreferences
            {
                PreferredCountries = countries,
                LinkedInProfileUrl = linkedInUrl,
                ResumeLinkedInProfileUrl = current.ResumeLinkedInProfileUrl,
                PreferredRoles = roles,
                WorkMode = workMode,
                SalaryExpectation = salaryExpectation,
                WorkAuthorization = workAuthorization
            };

            await preferences.SaveAsync(updated, ct);
            await audit.RecordAsync(user.UserId(), "JOB_SEARCH_PREFERENCES_UPDATED", "JobSearchPreferences", "local", "SUCCESS",
                $"Countries={updated.PreferredCountries.Count}; Roles={updated.PreferredRoles.Count}; WorkMode={updated.WorkMode ?? "(none)"}; LinkedIn={(updated.EffectiveLinkedInProfileUrl is null ? "missing" : "set")}", ct);
            return Results.Ok(ToJobSearchPreferencesResponse(updated));
        }).WithTags("Settings");

        api.MapGet("/settings/google-oauth", async (IGoogleOAuthSettingsProvider googleSettings, CancellationToken ct) =>
        {
            var current = await googleSettings.GetAsync(ct);
            var hasSecret = !string.IsNullOrWhiteSpace(current.ClientSecret);
            return new GoogleOAuthSettingsResponse(current.ClientId, current.RedirectUri, hasSecret,
                !string.IsNullOrWhiteSpace(current.ClientId) && hasSecret);
        }).WithTags("Settings");

        api.MapPut("/settings/google-oauth", async Task<IResult> (UpdateGoogleOAuthSettingsRequest request, ClaimsPrincipal user, IGoogleOAuthSettingsProvider googleSettings, AuditService audit, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.ClientId)) errors["clientId"] = ["Client ID is required."];
            if (string.IsNullOrWhiteSpace(request.RedirectUri)) errors["redirectUri"] = ["Redirect URI is required."];
            else if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out _)) errors["redirectUri"] = ["Redirect URI must be an absolute URL."];
            var current = await googleSettings.GetAsync(ct);
            var secret = string.IsNullOrWhiteSpace(request.ClientSecret) ? current.ClientSecret : request.ClientSecret.Trim();
            if (string.IsNullOrWhiteSpace(secret)) errors["clientSecret"] = ["Client secret is required the first time you configure Google OAuth."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var updated = new GoogleOAuthConfiguration(request.ClientId.Trim(), secret, request.RedirectUri.Trim());
            await googleSettings.SaveAsync(updated, ct);
            await audit.RecordAsync(user.UserId(), "GOOGLE_OAUTH_SETTINGS_UPDATED", "GoogleOAuth", "local", "SUCCESS", $"RedirectUri={updated.RedirectUri}; ClientIdLength={updated.ClientId.Length}; Secret={(string.IsNullOrWhiteSpace(request.ClientSecret) ? "unchanged" : "updated")}", ct);
            return Results.Ok(new GoogleOAuthSettingsResponse(updated.ClientId, updated.RedirectUri, true, true));
        }).WithTags("Settings");

        api.MapPost("/settings/master-resume", async Task<IResult> (HttpRequest request, ClaimsPrincipal user, IRuntimeSettingsProvider settings, ResumeAnalysisService analysis, AuditService audit, CancellationToken ct) =>
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
            // Only the path changes; LLM provider, model and API key must survive a resume upload.
            var updated = new RuntimeSettings
            {
                Mode = current.Mode,
                LlmProvider = current.LlmProvider,
                LlmDisplayName = current.LlmDisplayName,
                LlmBaseUrl = current.LlmBaseUrl,
                LlmApiKey = current.LlmApiKey,
                LlmModel = current.LlmModel,
                MasterResumePath = targetPath,
                QualificationThreshold = current.QualificationThreshold,
                ApprovalTtlHours = current.ApprovalTtlHours
            };
            await settings.SaveAsync(updated, ct);
            var summary = await analysis.AnalyzeAndSaveAsync(targetPath, user.UserId(), ct);
            await audit.RecordAsync(user.UserId(), "MASTER_RESUME_UPLOADED", "RuntimeSettings", "local", "SUCCESS", $"{file.FileName} ({file.Length} bytes) -> {targetPath}", ct);
            return Results.Ok(new { masterResumePath = targetPath, fileName = file.FileName, size = file.Length, analysis = summary });
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
        google.MapGet("/callback", async Task<IResult> (string? code, string? state, string? error, IGoogleOAuthService service, IConfiguration configuration, CancellationToken ct) =>
        {
            var webBaseUrl = configuration["Web:BaseUrl"] ?? "http://localhost:4200";
            if (!string.IsNullOrWhiteSpace(error))
            {
                return Results.Redirect($"{webBaseUrl}/settings?google=failed");
            }

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                return Results.BadRequest("Missing Google authorization code or state.");
            }

            var result = await service.CompleteAsync(code, state, ct);
            return result.Succeeded
                ? Results.Redirect($"{webBaseUrl}/settings?google=connected")
                : Results.BadRequest(result.Error ?? "Google authorization failed.");
        }).AllowAnonymous();
        google.MapPost("/revoke", async (IGoogleOAuthService service, CancellationToken ct) => { await service.RevokeAsync(ct); return Results.NoContent(); }).RequireAuthorization();

        var gmail = endpoints.MapGroup("/api/integrations/gmail").WithTags("Gmail").RequireAuthorization();
        gmail.MapGet("/labels", async (IGmailClient client, CancellationToken ct) =>
        {
            var labels = await client.ListLabelsAsync(ct);
            return Results.Ok(new { labels = labels });
        });
        gmail.MapGet("/messages", async (int? maxResults, string? pageToken, IGmailClient client, CancellationToken ct) =>
        {
            var results = await client.ListMessagesAsync(maxResults, pageToken, ct);
            return Results.Ok(new { messages = results.Messages, nextPageToken = results.NextPageToken, totalResults = results.TotalResults });
        });
        gmail.MapGet("/messages/search", async (string q, int? maxResults, string? pageToken, IGmailClient client, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { error = "Search query is required" });
            var results = await client.SearchMessagesAsync(q, maxResults, pageToken, ct);
            return Results.Ok(new { messages = results.Messages, nextPageToken = results.NextPageToken, totalResults = results.TotalResults });
        });
        gmail.MapGet("/messages/{messageId}", async (string messageId, IGmailClient client, CancellationToken ct) =>
        {
            var message = await client.GetMessageAsync(messageId, ct);
            return message is not null ? Results.Ok(message) : Results.NotFound();
        });

        return endpoints;
    }

    private static SettingsResponse ToSettingsResponse(RuntimeSettings current, OpenAiOptions openAi, string dataDirectory) =>
        new(current.Mode.ToString(), current.LlmProvider, current.LlmDisplayName, current.LlmBaseUrl,
            !string.IsNullOrWhiteSpace(current.LlmApiKey) || !string.IsNullOrWhiteSpace(openAi.ApiKey),
            current.LlmModel, !string.IsNullOrWhiteSpace(openAi.ApiKey), current.MasterResumePath,
            !string.IsNullOrWhiteSpace(current.MasterResumePath) && File.Exists(current.MasterResumePath), dataDirectory);

    private static JobSearchPreferencesResponse ToJobSearchPreferencesResponse(JobSearchPreferences preferences) =>
        new(preferences.PreferredCountries, preferences.LinkedInProfileUrl, preferences.ResumeLinkedInProfileUrl,
            preferences.EffectiveLinkedInProfileUrl, preferences.PreferredRoles, preferences.WorkMode,
            preferences.SalaryExpectation, preferences.WorkAuthorization);

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
