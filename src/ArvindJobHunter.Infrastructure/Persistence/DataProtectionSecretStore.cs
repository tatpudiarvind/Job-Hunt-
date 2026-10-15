using System.Security.Cryptography;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Persistence;

public sealed class SecretStoreDocument
{
    public Dictionary<string, string> Secrets { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DataProtectionSecretStore(
    IJsonStore<SecretStoreDocument> store,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<DataProtectionSecretStore>? logger = null) : ISecretStore
{
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("ArvindJobHunter.SecretStore.v1");
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = await store.LoadAsync(cancellationToken);
            if (!document.Secrets.TryGetValue(name, out var encrypted) || string.IsNullOrWhiteSpace(encrypted)) return null;
            try
            {
                return protector.Unprotect(encrypted);
            }
            catch (CryptographicException ex)
            {
                logger?.LogError(ex, "Secret {SecretName} could not be decrypted; ignoring the stored value", name);
                return null;
            }
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(string name, string? value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var document = await store.LoadAsync(cancellationToken);
            var secrets = new Dictionary<string, string>(document.Secrets, StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value))
            {
                secrets.Remove(name);
            }
            else
            {
                secrets[name] = protector.Protect(value.Trim());
            }

            await store.SaveAsync(new SecretStoreDocument { Secrets = secrets }, cancellationToken);
            logger?.LogDebug("Secret {SecretName} {Operation}", name, string.IsNullOrWhiteSpace(value) ? "removed" : "saved");
        }
        finally { gate.Release(); }
    }
}
