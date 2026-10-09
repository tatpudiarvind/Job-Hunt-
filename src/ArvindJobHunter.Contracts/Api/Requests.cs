using System.ComponentModel.DataAnnotations;

namespace ArvindJobHunter.Contracts.Api;

public sealed record AuthRequest([property: Required] string Username, [property: Required] string Password);

public sealed record ResetPasswordRequest([property: Required] string Username, [property: Required] string NewPassword);

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

public sealed record SettingsResponse(string Mode, string LlmProvider, string LlmDisplayName, string LlmBaseUrl, bool LlmConfigured, string LlmModel, bool OpenAiConfigured, string MasterResumePath, bool MasterResumeExists, string DataDirectory);

public sealed record GoogleOAuthSettingsResponse(string ClientId, string ClientSecret, string RedirectUri, bool Configured);

public sealed record UpdateSettingsRequest(string? Mode, string? LlmProvider, string? LlmDisplayName, string? LlmBaseUrl, string? LlmApiKey, string? LlmModel, string? MasterResumePath);

public sealed record UpdateGoogleOAuthSettingsRequest(
    [property: Required] string ClientId,
    [property: Required] string ClientSecret,
    [property: Required] string RedirectUri);

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
