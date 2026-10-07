namespace ArvindJobHunter.Domain.Entities;

public sealed record AgentRun(
    Guid Id,
    string AgentName,
    string Workflow,
    Guid? JobId,
    AgentRunStatus Status,
    ExecutionMode Mode,
    Guid StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error,
    string LlmProvider,
    IReadOnlyList<AgentToolCall> ToolCalls)
{
    public static AgentRun Start(string agentName, string workflow, Guid? jobId, ExecutionMode mode, Guid startedBy, string llmProvider) =>
        new(Guid.NewGuid(), agentName, workflow, jobId, AgentRunStatus.RUNNING, mode, startedBy, DateTimeOffset.UtcNow, null, null, llmProvider, []);

    public AgentRun WithToolCall(AgentToolCall call) => this with { ToolCalls = [.. ToolCalls, call] };

    public AgentRun Complete() => this with { Status = AgentRunStatus.COMPLETED, CompletedAt = DateTimeOffset.UtcNow };

    public AgentRun Fail(string error) => this with { Status = AgentRunStatus.FAILED, CompletedAt = DateTimeOffset.UtcNow, Error = error };
}

public sealed record AgentToolCall(
    string ToolName,
    string InputSummary,
    string OutputSummary,
    bool Succeeded,
    DateTimeOffset At,
    long DurationMs);
