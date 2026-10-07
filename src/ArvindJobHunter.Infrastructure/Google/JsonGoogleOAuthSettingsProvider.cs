using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Google;

public sealed class JsonGoogleOAuthSettingsProvider(string appSettingsPath, GoogleOAuthConfiguration defaults) : IGoogleOAuthSettingsProvider
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private GoogleOAuthConfiguration? cached;

    public async Task<GoogleOAuthConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null) return cached;
            cached = await LoadFromFileAsync(cancellationToken);
            return cached;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(GoogleOAuthConfiguration configuration, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var stream = File.Open(appSettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement.Clone();

            var updatedJson = JsonSerializer.Serialize(BuildRoot(root, configuration), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(appSettingsPath, updatedJson + Environment.NewLine, cancellationToken);
            cached = configuration;
        }
        finally { gate.Release(); }
    }

    private async Task<GoogleOAuthConfiguration> LoadFromFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(appSettingsPath)) return defaults;
        using var stream = File.Open(appSettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("Google", out var google)) return defaults;

        return new GoogleOAuthConfiguration(
            google.TryGetProperty("ClientId", out var clientId) ? clientId.GetString() ?? defaults.ClientId : defaults.ClientId,
            google.TryGetProperty("ClientSecret", out var clientSecret) ? clientSecret.GetString() ?? defaults.ClientSecret : defaults.ClientSecret,
            google.TryGetProperty("RedirectUri", out var redirectUri) ? redirectUri.GetString() ?? defaults.RedirectUri : defaults.RedirectUri);
    }

    private static Dictionary<string, object?> BuildRoot(JsonElement root, GoogleOAuthConfiguration configuration)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            result[property.Name] = property.NameEquals("Google")
                ? new Dictionary<string, object?>
                {
                    ["ClientId"] = configuration.ClientId,
                    ["ClientSecret"] = configuration.ClientSecret,
                    ["RedirectUri"] = configuration.RedirectUri
                }
                : DeserializeElement(property.Value);
        }

        if (!result.ContainsKey("Google"))
        {
            result["Google"] = new Dictionary<string, object?>
            {
                ["ClientId"] = configuration.ClientId,
                ["ClientSecret"] = configuration.ClientSecret,
                ["RedirectUri"] = configuration.RedirectUri
            };
        }

        return result;
    }

    private static object? DeserializeElement(JsonElement element)
        => JsonSerializer.Deserialize<object?>(element.GetRawText());
}
