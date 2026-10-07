using System.Collections.Concurrent;
using System.Security.Cryptography;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Application.Features;

public sealed class LocalAuthenticationService(IJsonStore<LocalAccountRecord> store) : ILocalAuthenticationService
{
    private const int Iterations = 210_000;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, LocalSession> sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    private LocalAccountRecord? account;

    public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken) =>
        (await AccountAsync(cancellationToken)).Username.Length > 0;

    public async Task<bool> SetupAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || password.Length < 12) return false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            account ??= await store.LoadAsync(cancellationToken);
            if (account.Username.Length > 0) return false;
            var salt = RandomNumberGenerator.GetBytes(16);
            account = new LocalAccountRecord
            {
                Username = username.Trim(),
                Salt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(Hash(password, salt))
            };
            await store.SaveAsync(account, cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSession?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var configured = await AccountAsync(cancellationToken);
        if (configured.Username.Length == 0
            || !string.Equals(configured.Username, username.Trim(), StringComparison.OrdinalIgnoreCase)
            || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(configured.PasswordHash), Hash(password, Convert.FromBase64String(configured.Salt))))
        {
            return null;
        }

        PruneExpired();
        var session = new LocalSession(LocalUser.Id, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), DateTimeOffset.UtcNow.Add(SessionLifetime));
        sessions[session.Token] = session;
        return session;
    }

    public bool TryGetUser(string token, out LocalSession session)
    {
        if (sessions.TryGetValue(token, out var found) && found.ExpiresAt > DateTimeOffset.UtcNow)
        {
            session = found;
            return true;
        }

        if (found is not null) sessions.TryRemove(token, out _);
        session = null!;
        return false;
    }

    public void Logout(string token) => sessions.TryRemove(token, out _);

    private void PruneExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var expired in sessions.Where(s => s.Value.ExpiresAt <= now).Select(s => s.Key).ToList())
        {
            sessions.TryRemove(expired, out _);
        }
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
