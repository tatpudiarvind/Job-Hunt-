using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Persistence;

public sealed class JsonRuntimeSettingsProvider(IJsonStore<RuntimeSettings> store, RuntimeSettings defaults) : IRuntimeSettingsProvider, ICacheInvalidatable
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
            var loaded = await store.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(loaded.LlmProvider))
            {
                cache = defaults;
            }
            else
            {
                cache = new RuntimeSettings
                {
                    Mode = loaded.Mode,
                    LlmProvider = loaded.LlmProvider,
                    LlmDisplayName = string.IsNullOrWhiteSpace(loaded.LlmDisplayName) ? loaded.LlmProvider : loaded.LlmDisplayName,
                    LlmBaseUrl = string.IsNullOrWhiteSpace(loaded.LlmBaseUrl) ? defaults.LlmBaseUrl : loaded.LlmBaseUrl,
                    LlmApiKey = string.IsNullOrWhiteSpace(loaded.LlmApiKey) ? defaults.LlmApiKey : loaded.LlmApiKey,
                    LlmModel = string.IsNullOrWhiteSpace(loaded.LlmModel) ? defaults.LlmModel : loaded.LlmModel,
                    MasterResumePath = loaded.MasterResumePath,
                    QualificationThreshold = loaded.QualificationThreshold,
                    ApprovalTtlHours = loaded.ApprovalTtlHours
                };
            }
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
        }
        finally { gate.Release(); }
    }
}
