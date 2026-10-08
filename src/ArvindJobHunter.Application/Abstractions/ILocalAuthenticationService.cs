namespace ArvindJobHunter.Application.Abstractions;

public sealed record LocalSession(Guid UserId, string Token, DateTimeOffset ExpiresAt);

public sealed class LocalAccountRecord
{
    public string Username { get; init; } = "";
    public string Salt { get; init; } = "";
    public string PasswordHash { get; init; } = "";
}

public interface ILocalAuthenticationService
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);
    Task<bool> SetupAsync(string username, string password, CancellationToken cancellationToken);
    Task<bool> SignupAsync(string username, string password, CancellationToken cancellationToken);
    Task<bool> ResetPasswordAsync(string username, string newPassword, CancellationToken cancellationToken);
    Task<LocalSession?> LoginAsync(string username, string password, CancellationToken cancellationToken);
    bool TryGetUser(string token, out LocalSession session);
    void Logout(string token);
}
