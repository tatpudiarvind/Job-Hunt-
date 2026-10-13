using System.Text;
using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Application.Abstractions;

public sealed record RuntimeSettings
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

    // Records print every property by default; the API key must never end up in a log line or exception message.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Mode = {Mode}, LlmProvider = {LlmProvider}, LlmDisplayName = {LlmDisplayName}, LlmBaseUrl = {LlmBaseUrl}, ")
            .Append($"LlmApiKey = {(string.IsNullOrWhiteSpace(LlmApiKey) ? "<none>" : "***")}, LlmModel = {LlmModel}, MasterResumePath = {MasterResumePath}, ")
            .Append($"QualificationThreshold = {QualificationThreshold}, ApprovalTtlHours = {ApprovalTtlHours}");
        return true;
    }
}

public interface IRuntimeSettingsProvider
{
    Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(RuntimeSettings settings, CancellationToken cancellationToken);
}
