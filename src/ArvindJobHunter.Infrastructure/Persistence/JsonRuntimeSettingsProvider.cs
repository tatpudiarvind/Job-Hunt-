using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>
/// Runtime settings: the Settings page writes <c>settings.json</c>, which wins for the fields it edits.
/// Until that file exists the configured <c>Runtime:*</c> values apply. Fields the UI cannot edit
/// (qualification threshold, approval TTL) always come from configuration.
/// </summary>
public sealed class JsonRuntimeSettingsProvider(IJsonStore<RuntimeSettings> store, RuntimeSettings defaults, ILogger<JsonRuntimeSettingsProvider>? logger = null) : IRuntimeSettingsProvider, ICacheInvalidatable
{
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
                cache = defaults;
                logger?.LogInformation("No saved runtime settings yet; using configured defaults: {Settings}", Describe(cache));
                return cache;
            }

            var loaded = await store.LoadAsync(cancellationToken);
            var provider = string.IsNullOrWhiteSpace(loaded.LlmProvider) ? defaults.LlmProvider : loaded.LlmProvider;
            cache = loaded with
            {
                LlmProvider = provider,
                LlmDisplayName = string.IsNullOrWhiteSpace(loaded.LlmDisplayName) ? provider : loaded.LlmDisplayName,
                LlmBaseUrl = string.IsNullOrWhiteSpace(loaded.LlmBaseUrl) ? defaults.LlmBaseUrl : loaded.LlmBaseUrl,
                LlmApiKey = string.IsNullOrWhiteSpace(loaded.LlmApiKey) ? defaults.LlmApiKey : loaded.LlmApiKey,
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
            cache = settings;
            await store.SaveAsync(settings, cancellationToken);
            logger?.LogDebug("Runtime settings saved: {Settings}", Describe(settings));
        }
        finally { gate.Release(); }
    }

    /// <summary>Human-readable summary for logs; never includes the API key itself.</summary>
    public static string Describe(RuntimeSettings settings) =>
        $"mode {settings.Mode}, LLM {settings.LlmProvider} ({settings.LlmDisplayName}, model {settings.LlmModel}, API key {(string.IsNullOrWhiteSpace(settings.LlmApiKey) ? "not set" : "set")}), " +
        $"master resume {(string.IsNullOrWhiteSpace(settings.MasterResumePath) ? "not configured" : settings.MasterResumePath)}, " +
        $"qualification threshold {settings.QualificationThreshold}, approval TTL {settings.ApprovalTtlHours} h";
}
