using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Google;

public sealed class GoogleOAuthSettingsDocument
{
    public string ClientId { get; init; } = "";
    public string RedirectUri { get; init; } = "http://localhost:5228/api/integrations/google/callback";
}

public sealed class JsonGoogleOAuthSettingsProvider(
    IJsonStore<GoogleOAuthSettingsDocument> store,
    ISecretStore secrets,
    string appSettingsPath,
    GoogleOAuthConfiguration defaults,
    ILogger<JsonGoogleOAuthSettingsProvider>? logger = null) : IGoogleOAuthSettingsProvider
{
    private const string GoogleClientSecretName = "google.clientSecret";
    private readonly SemaphoreSlim gate = new(1, 1);
    private GoogleOAuthConfiguration? cached;

    public async Task<GoogleOAuthConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && !string.IsNullOrWhiteSpace(cached.ClientId)) return cached;
            cached = await LoadAsync(cancellationToken);
            return cached;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(GoogleOAuthConfiguration configuration, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var secret = string.IsNullOrWhiteSpace(configuration.ClientSecret)
                ? await secrets.GetAsync(GoogleClientSecretName, cancellationToken) ?? defaults.ClientSecret
                : configuration.ClientSecret.Trim();
            await secrets.SaveAsync(GoogleClientSecretName, secret, cancellationToken);
            await store.SaveAsync(new GoogleOAuthSettingsDocument
            {
                ClientId = configuration.ClientId.Trim(),
                RedirectUri = configuration.RedirectUri.Trim()
            }, cancellationToken);
            cached = configuration with { ClientSecret = secret };
            logger?.LogInformation("Google OAuth client settings were saved to protected local storage.");
        }
        finally { gate.Release(); }
    }

    private async Task<GoogleOAuthConfiguration> LoadAsync(CancellationToken cancellationToken)
    {
        var secret = await secrets.GetAsync(GoogleClientSecretName, cancellationToken) ?? defaults.ClientSecret;
        if (await store.ExistsAsync(cancellationToken))
        {
            var saved = await store.LoadAsync(cancellationToken);
            return new GoogleOAuthConfiguration(
                string.IsNullOrWhiteSpace(saved.ClientId) ? defaults.ClientId : saved.ClientId,
                secret,
                string.IsNullOrWhiteSpace(saved.RedirectUri) ? defaults.RedirectUri : saved.RedirectUri);
        }

        var migrated = await LoadLegacySettingsAsync(cancellationToken);
        return new GoogleOAuthConfiguration(
            migrated?.ClientId ?? defaults.ClientId,
            string.IsNullOrWhiteSpace(secret) ? migrated?.ClientSecret ?? defaults.ClientSecret : secret,
            migrated?.RedirectUri ?? defaults.RedirectUri);
    }

    private async Task<GoogleOAuthConfiguration?> LoadLegacySettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appSettingsPath)) return null;
        using var stream = File.Open(appSettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("Google", out var google)) return null;

        return new GoogleOAuthConfiguration(
            google.TryGetProperty("ClientId", out var clientId) ? clientId.GetString() ?? defaults.ClientId : defaults.ClientId,
            google.TryGetProperty("ClientSecret", out var clientSecret) ? clientSecret.GetString() ?? defaults.ClientSecret : defaults.ClientSecret,
            google.TryGetProperty("RedirectUri", out var redirectUri) ? redirectUri.GetString() ?? defaults.RedirectUri : defaults.RedirectUri);
    }
}
