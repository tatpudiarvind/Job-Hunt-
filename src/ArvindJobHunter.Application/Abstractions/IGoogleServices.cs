namespace ArvindJobHunter.Application.Abstractions;

public sealed record GoogleConnectionStatus(bool Configured, bool Connected, string? AccountEmail);

public interface IGoogleOAuthService
{
    Task<GoogleConnectionStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<Uri> CreateAuthorizationUriAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(string code, string state, CancellationToken cancellationToken);
    Task RevokeAsync(CancellationToken cancellationToken);
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

public sealed record GmailResult(bool Succeeded, string? ExternalId, string? Error);

public interface IGmailClient
{
    Task<GmailResult> CreateDraftAsync(string to, string subject, string body, CancellationToken cancellationToken);
    Task<GmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}
