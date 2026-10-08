using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Agents.Llm;

/// <summary>
/// Resolves the provider selected in runtime settings. An unknown provider name falls back to Demo; OpenAI without an
/// API key is still returned (so runs fail loudly with an actionable message instead of silently using Demo) and is logged.
/// </summary>
public sealed class LlmProviderResolver(IEnumerable<ILlmProvider> providers, IRuntimeSettingsProvider settings, ILogger<LlmProviderResolver> logger) : ILlmProviderResolver
{
    private string? lastWarning;

    public async Task<ILlmProvider> ResolveAsync(CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var list = providers.ToList();
        var requested = list.FirstOrDefault(p => string.Equals(p.Name, current.LlmProvider, StringComparison.OrdinalIgnoreCase));
        if (requested is OpenAiLlmProvider openAi && openAi.CanResolve(current)) return Healthy(openAi);
        if (requested is OpenAiLlmProvider)
        {
            WarnOnce("openai-without-key", "LLM provider OpenAI is selected but no API key is configured; agent runs will fail until a key is set in Settings");
            return requested;
        }

        if (requested is { IsConfigured: true }) return Healthy(requested);
        WarnOnce($"unavailable:{current.LlmProvider}", "LLM provider {Provider} is not available; falling back to the offline Demo provider", current.LlmProvider);
        return list.First(p => p.Name == "Demo");
    }

    private ILlmProvider Healthy(ILlmProvider provider)
    {
        Volatile.Write(ref lastWarning, null);
        return provider;
    }

    // The resolver runs on every dashboard load and agent run; only log when the situation changes.
    private void WarnOnce(string key, string message, params object?[] args)
    {
        if (Interlocked.Exchange(ref lastWarning, key) != key) logger.LogWarning(message, args);
    }
}
