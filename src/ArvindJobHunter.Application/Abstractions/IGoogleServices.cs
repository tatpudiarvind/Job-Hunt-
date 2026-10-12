namespace ArvindJobHunter.Application.Abstractions;

public sealed record GoogleConnectionStatus(bool Configured, bool Connected, string? AccountEmail);

public sealed record GoogleOAuthConfiguration(string ClientId, string ClientSecret, string RedirectUri);

public sealed record GoogleOAuthCompletionResult(bool Succeeded, string? Error);

public interface IGoogleOAuthSettingsProvider
{
    Task<GoogleOAuthConfiguration> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(GoogleOAuthConfiguration configuration, CancellationToken cancellationToken);
}

public interface IGoogleOAuthService
{
    Task<GoogleConnectionStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<Uri> CreateAuthorizationUriAsync(Guid userId, CancellationToken cancellationToken);
    Task<GoogleOAuthCompletionResult> CompleteAsync(string code, string state, CancellationToken cancellationToken);
    Task RevokeAsync(CancellationToken cancellationToken);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed record GmailResult(bool Succeeded, string? ExternalId, string? Error);

public sealed record GmailLabel(string Id, string Name, int? MessageCount, int? ThreadCount);

public sealed record GmailMessageHeader(string Name, string Value);

public sealed record GmailMessagePart(
    string? MimeType,
    string? FileName,
    string? Data,
    IReadOnlyList<GmailMessagePart> Parts = default!);

public sealed record GmailMessagePayload(
    string? MimeType,
    IReadOnlyList<GmailMessageHeader>? Headers,
    string? Body,
    IReadOnlyList<GmailMessagePart>? Parts);

public sealed record GmailMessageSummary(
    string Id,
    string ThreadId,
    IReadOnlyList<string> Labels,
    bool IsSpam,
    bool IsStarred,
    bool IsUnread,
    DateTimeOffset ReceivedAt,
    string? From,
    string? Subject,
    string? Preview);

public sealed record GmailMessage(
    string Id,
    string ThreadId,
    IReadOnlyList<string> Labels,
    bool IsSpam,
    bool IsStarred,
    bool IsUnread,
    DateTimeOffset ReceivedAt,
    string? Snippet,
    GmailMessagePayload? Payload,
    string? TextContent,
    string? HtmlContent);

public sealed record GmailSearchResults(
    IReadOnlyList<GmailMessageSummary> Messages,
    string? NextPageToken,
    int TotalResults);

public interface IGmailClient
{
    // Send Operations
    Task<GmailResult> CreateDraftAsync(string to, string subject, string body, CancellationToken cancellationToken);
    Task<GmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken);

    // Read Operations
    Task<IReadOnlyList<GmailLabel>> ListLabelsAsync(CancellationToken cancellationToken);
    Task<GmailSearchResults> ListMessagesAsync(int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default);
    Task<GmailSearchResults> SearchMessagesAsync(string query, int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default);
    Task<GmailMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken);
}
