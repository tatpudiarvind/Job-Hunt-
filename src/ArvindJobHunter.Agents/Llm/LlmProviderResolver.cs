using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Agents.Llm;

/// <summary>Resolves the active provider from runtime settings. Falls back to Demo whenever the requested provider is not configured.</summary>
public sealed class LlmProviderResolver(IEnumerable<ILlmProvider> providers, IRuntimeSettingsProvider settings) : ILlmProviderResolver
{
    public async Task<ILlmProvider> ResolveAsync(CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var list = providers.ToList();
        var requested = list.FirstOrDefault(p => string.Equals(p.Name, current.LlmProvider, StringComparison.OrdinalIgnoreCase));
        if (requested is OpenAiLlmProvider openAi && openAi.CanResolve(current)) return openAi;
        if (requested is { IsConfigured: true }) return requested;
        return list.First(p => p.Name == "Demo");
    }
}
