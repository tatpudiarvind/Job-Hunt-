using System.ComponentModel.DataAnnotations;

namespace ArvindJobHunter.Contracts.Api;

public sealed record AuthRequest([property: Required] string Username, [property: Required] string Password);

public sealed record ResetPasswordRequest([property: Required] string Username, [property: Required] string CurrentPassword, [property: Required] string NewPassword);

public sealed record AuthStatusResponse(bool Configured, bool Authenticated);

public sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId);

public sealed record CreateFactRequest(
    [property: Required] string FactType,
    [property: Required] string Name,
    [property: Required] string Value,
    string? SourceReference);

public sealed record CreateJobRequest(
    [property: Required, MaxLength(200)] string Title,
    [property: Required, MaxLength(200)] string Company,
    [property: MaxLength(200)] string? Location,
    [property: MaxLength(100)] string? Source,
    [property: MaxLength(2000)] string? Url,
    [property: Required] string Description);

public sealed record ApprovalDecisionRequest(string? Note);

public sealed record ExecuteApprovalRequest([property: Required] string IdempotencyKey);

public sealed record TransitionApplicationRequest([property: Required] string Status, string? Reason);

public sealed record CreateEmailDraftRequest(
    Guid? JobId,
    [property: Required] string Kind,
    [property: Required] string To,
    [property: Required] string Subject,
    [property: Required] string Body);

public sealed record UpdateEmailDraftRequest(
    [property: Required] string To,
    [property: Required] string Subject,
    [property: Required] string Body);

public sealed record SettingsResponse(string Mode, string LlmProvider, string LlmDisplayName, string LlmBaseUrl, bool LlmConfigured, string LlmModel, bool OpenAiConfigured, string MasterResumePath, bool MasterResumeExists, string DataDirectory, string? MasterResumeWarning = null, IReadOnlyDictionary<string, string>? CountryResumePaths = null);

public sealed record SetJobCountryRequest(string? CountryCode);

public sealed record SetResumeTemplateRequest(string Template);

public sealed record GoogleOAuthSettingsResponse(string ClientId, string RedirectUri, bool ClientSecretConfigured, bool Configured);

public sealed record JobSearchPreferencesResponse(
    IReadOnlyList<string> PreferredCountries,
    string? LinkedInProfileUrl,
    string? ResumeLinkedInProfileUrl,
    string? EffectiveLinkedInProfileUrl,
    IReadOnlyList<string> PreferredRoles,
    string? WorkMode,
    string? SalaryExpectation,
    string? WorkAuthorization);

public sealed record UpdateSettingsRequest(string? Mode, string? LlmProvider, string? LlmDisplayName, string? LlmBaseUrl, string? LlmApiKey, string? LlmModel, string? MasterResumePath);

public sealed record UpdateJobSearchPreferencesRequest(
    IReadOnlyList<string>? PreferredCountries,
    string? LinkedInProfileUrl,
    IReadOnlyList<string>? PreferredRoles,
    string? WorkMode,
    string? SalaryExpectation,
    string? WorkAuthorization);

public sealed record UpdateGoogleOAuthSettingsRequest(
    [property: Required] string ClientId,
    string? ClientSecret,
    [property: Required] string RedirectUri);

public sealed record ResumePreviewParagraph(int Index, string Text, string? Style);

public sealed record ResumePreviewResponse(Guid Id, Guid JobId, string Status, IReadOnlyList<ResumePreviewParagraph> Paragraphs, DateTimeOffset GeneratedAt, TemplateFidelityResponse? TemplateFidelity = null);

public sealed record TemplateFidelityResponse(bool TemplatePreserved, IReadOnlyList<string> Issues, int MasterPageEstimate, int TailoredPageEstimate);

/// <summary>An error or warning reported by the browser so it lands in the same Markdown log as server events.</summary>
public sealed record ClientLogRequest(
    string? Level,
    [property: Required, MaxLength(2000)] string Message,
    [property: MaxLength(500)] string? Url,
    [property: MaxLength(8000)] string? Stack);

public sealed record DashboardResponse(
    int Jobs,
    int QualifiedJobs,
    int Applications,
    int PendingApprovals,
    int AgentRuns,
    int ExternalActions,
    int VerifiedFacts,
    string Mode,
    string LlmProvider);

// Browser auto-apply: API <-> Worker and API <-> Web.
public sealed record FillFieldDto(string Label, string? Value, bool Required, string Source);

public sealed record FillPreviewRequest(
    string Url,
    string ResumeFilePath,
    string? CoverLetterText,
    IReadOnlyDictionary<string, string> Answers,
    string? Adapter = null,
    string? CoverLetterFilePath = null);

public sealed record FillPreviewResponse(
    string SessionId,
    string Adapter,
    IReadOnlyList<FillFieldDto> Fields,
    IReadOnlyList<string> UnansweredFields,
    string? ScreenshotPath,
    string? Blocker = null);

public sealed record SubmitRequest(string SessionId, string ExpectedPayloadHash, string IdempotencyKey);

public sealed record SubmitResponse(bool Submitted, string? ConfirmationText, string? ScreenshotPath, string? Error = null);

public sealed record SiteSettingsDto(string Adapter, bool Enabled, int DailyCap, int SubmittedToday = 0);

public sealed record UpdateSiteSettingsRequest(IReadOnlyList<SiteSettingsDto> Sites);

public sealed record PrepareSubmissionRequest(Guid? ResumeVersionId = null, string? Adapter = null);

public sealed record AnswerSubmissionRequest(IReadOnlyDictionary<string, string> Answers);
