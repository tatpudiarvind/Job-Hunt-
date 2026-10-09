using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Application.Abstractions;

public sealed class RuntimeSettings
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.DEMO;
    public string LlmProvider { get; init; } = "Demo";
    public string LlmDisplayName { get; init; } = "Demo";
    public string LlmBaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string LlmApiKey { get; init; } = "";
    public string LlmModel { get; init; } = "gpt-4o-mini";
    public string MasterResumePath { get; init; } = "";
    public int QualificationThreshold { get; init; } = 60;
    public int ApprovalTtlHours { get; init; } = 24;
}

public interface IRuntimeSettingsProvider
{
    Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(RuntimeSettings settings, CancellationToken cancellationToken);
}
