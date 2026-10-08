using System.Collections.Concurrent;
using System.Security.Cryptography;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class LocalAuthenticationService(IJsonStore<LocalAccountRecord> store, ILogger<LocalAuthenticationService> logger) : ILocalAuthenticationService
{
    private const int Iterations = 210_000;
    private const int MinimumPasswordLength = 12;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, LocalSession> sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    private LocalAccountRecord? account;

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        (await AccountAsync(cancellationToken)).Username.Length > 0;

    public Task<bool> SetupAsync(string username, string password, CancellationToken cancellationToken) =>
        SignupAsync(username, password, cancellationToken);

    public async Task<bool> SignupAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsAcceptablePassword(password))
        {
            logger.LogWarning("Sign-up rejected: a username and a password of at least {MinimumLength} characters are required", MinimumPasswordLength);
            return false;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            account ??= await store.LoadAsync(cancellationToken);
            if (account.Username.Length > 0)
            {
                logger.LogWarning("Sign-up rejected: the local account already exists");
                return false;
            }

            account = CreateAccount(username, password);
            await store.SaveAsync(account, cancellationToken);
            logger.LogInformation("Local account {Username} created", account.Username);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> ResetPasswordAsync(string username, string newPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsAcceptablePassword(newPassword))
        {
            logger.LogWarning("Password reset rejected: a username and a new password of at least {MinimumLength} characters are required", MinimumPasswordLength);
            return false;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            account ??= await store.LoadAsync(cancellationToken);
            if (account.Username.Length == 0 || !string.Equals(account.Username, username.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // The typed name is not logged: people sometimes type a password into the username box.
                logger.LogWarning("Password reset rejected: the username does not match the local account");
                return false;
            }

            account = CreateAccount(account.Username, newPassword);
            var revoked = sessions.Count;
            sessions.Clear();
            await store.SaveAsync(account, cancellationToken);
            logger.LogWarning("Password reset for {Username}; {RevokedSessions} active session(s) revoked", account.Username, revoked);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSession?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var configured = await AccountAsync(cancellationToken);
        var failure = configured.Username.Length == 0 ? "no local account exists yet"
            : string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) ? "username or password missing"
            : !string.Equals(configured.Username, username.Trim(), StringComparison.OrdinalIgnoreCase) ? "unknown username"
            : !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(configured.PasswordHash), Hash(password, Convert.FromBase64String(configured.Salt))) ? "wrong password"
            : null;
        if (failure is not null)
        {
            // Only the account's own name is logged; an unmatched entry may be a password typed into the username box.
            if (failure == "wrong password") logger.LogWarning("Login failed for {Username}: {Reason}", configured.Username, failure);
            else logger.LogWarning("Login failed: {Reason}", failure);
            return null;
        }

        PruneExpired();
        var session = new LocalSession(LocalUser.Id, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow.Add(SessionLifetime));
        sessions[session.Token] = session;
        logger.LogInformation("Login succeeded for {Username}; session valid until {ExpiresAt:u}", configured.Username, session.ExpiresAt);
        return session;
    }

    public bool TryGetUser(string token, out LocalSession session)
    {
        if (sessions.TryGetValue(token, out var found) && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            session = found;
            return true;
        }

        if (found is not null && sessions.TryRemove(token, out _))
        {
            logger.LogInformation("Session expired at {ExpiresAt:u}; the user must sign in again", found.ExpiresAt);
        }

        session = null!;
        return false;
    }

    public void Logout(string token)
    {
        if (sessions.TryRemove(token, out _)) logger.LogInformation("Signed out; session ended");
    }

    private static bool IsAcceptablePassword(string? password) => password is { Length: >= MinimumPasswordLength };

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in sessions.Where(s => s.Value.ExpiresAt <= now).Select(s => s.Key).ToList())
        {
            sessions.TryRemove(expired, out _);
        }
    }

    private static LocalAccountRecord CreateAccount(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new LocalAccountRecord
        {
            Username = username.Trim(),
            Salt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(Hash(password, salt))
        };
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, 32);

    private async Task<LocalAccountRecord> AccountAsync(CancellationToken cancellationToken)
    {
        if (account is not null) return account;
        await gate.WaitAsync(cancellationToken);
        try { return account ??= await store.LoadAsync(cancellationToken); }
        finally { gate.Release(); }
    }
}
