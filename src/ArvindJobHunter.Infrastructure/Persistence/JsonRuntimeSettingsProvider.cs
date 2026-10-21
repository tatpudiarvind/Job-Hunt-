using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>
/// Runtime settings: the Settings page writes <c>settings.json</c>, which wins for the fields it edits.
/// Until that file exists the configured <c>Runtime:*</c> values apply. Fields the UI cannot edit
/// (qualification threshold, approval TTL) always come from configuration.
/// </summary>
public sealed class JsonRuntimeSettingsProvider(IJsonStore<RuntimeSettings> store, ISecretStore secrets, RuntimeSettings defaults, ILogger<JsonRuntimeSettingsProvider>? logger = null) : IRuntimeSettingsProvider, ICacheInvalidatable
{
    private const string LlmApiKeySecretName = "runtime.llmApiKey";
    private readonly SemaphoreSlim gate = new(1, 1);
    private RuntimeSettings? cache;

    public void Invalidate() { gate.Wait(); try { cache = null; } finally { gate.Release(); } }

    public async Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken)
    {
        if (cache is not null) return cache;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache is not null) return cache;
            if (!await store.ExistsAsync(cancellationToken))
            {
                cache = defaults with
                {
                    LlmApiKey = await secrets.GetAsync(LlmApiKeySecretName, cancellationToken) ?? defaults.LlmApiKey
                };
                logger?.LogInformation("No saved runtime settings yet; using configured defaults: {Settings}", Describe(cache));
                return cache;
            }

            var loaded = await store.LoadAsync(cancellationToken);
            var storedKey = await secrets.GetAsync(LlmApiKeySecretName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(loaded.LlmApiKey))
            {
                // Legacy settings.json kept the key in plain text: move it into the protected store once, then scrub the file.
                if (string.IsNullOrWhiteSpace(storedKey))
                {
                    storedKey = loaded.LlmApiKey.Trim();
                    await secrets.SaveAsync(LlmApiKeySecretName, storedKey, cancellationToken);
                }
                await store.SaveAsync(loaded with { LlmApiKey = string.Empty }, cancellationToken);
                logger?.LogInformation("Migrated the LLM API key from settings.json into the protected secret store");
            }

            var provider = string.IsNullOrWhiteSpace(loaded.LlmProvider) ? defaults.LlmProvider : loaded.LlmProvider;
            cache = loaded with
            {
                LlmProvider = provider,
                LlmDisplayName = string.IsNullOrWhiteSpace(loaded.LlmDisplayName) ? provider : loaded.LlmDisplayName,
                LlmBaseUrl = string.IsNullOrWhiteSpace(loaded.LlmBaseUrl) ? defaults.LlmBaseUrl : loaded.LlmBaseUrl,
                LlmApiKey = storedKey ?? defaults.LlmApiKey,
                LlmModel = string.IsNullOrWhiteSpace(loaded.LlmModel) ? defaults.LlmModel : loaded.LlmModel,
                QualificationThreshold = defaults.QualificationThreshold,
                ApprovalTtlHours = defaults.ApprovalTtlHours
            };
            logger?.LogInformation("Runtime settings loaded: {Settings}", Describe(cache));
            return cache;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(RuntimeSettings settings, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(settings.LlmApiKey)
                ? await secrets.GetAsync(LlmApiKeySecretName, cancellationToken) ?? defaults.LlmApiKey
                : settings.LlmApiKey.Trim();
            cache = settings with { LlmApiKey = apiKey };
            await secrets.SaveAsync(LlmApiKeySecretName, apiKey, cancellationToken);
            await store.SaveAsync(settings with { LlmApiKey = string.Empty }, cancellationToken);
            logger?.LogDebug("Runtime settings saved: {Settings}", Describe(cache));
        }
        finally { gate.Release(); }
    }

    /// <summary>Human-readable summary for logs; never includes the API key itself.</summary>
    public static string Describe(RuntimeSettings settings) =>
        $"mode {settings.Mode}, LLM {settings.LlmProvider} ({settings.LlmDisplayName}, model {settings.LlmModel}, API key {(string.IsNullOrWhiteSpace(settings.LlmApiKey) ? "not set" : "set")}), " +
        $"master resume {(string.IsNullOrWhiteSpace(settings.MasterResumePath) ? "not configured" : settings.MasterResumePath)}, " +
        $"qualification threshold {settings.QualificationThreshold}, approval TTL {settings.ApprovalTtlHours} h";
}
