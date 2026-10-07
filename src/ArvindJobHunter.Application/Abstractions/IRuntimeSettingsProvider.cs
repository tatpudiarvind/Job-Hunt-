using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Application.Abstractions;

public sealed class RuntimeSettings
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.DEMO;
    public string LlmProvider { get; init; } = "Demo";
    public string MasterResumePath { get; init; } = "";
    public int QualificationThreshold { get; init; } = 60;
    public int ApprovalTtlHours { get; init; } = 24;
}

public interface IRuntimeSettingsProvider
{
    Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(RuntimeSettings settings, CancellationToken cancellationToken);
}
