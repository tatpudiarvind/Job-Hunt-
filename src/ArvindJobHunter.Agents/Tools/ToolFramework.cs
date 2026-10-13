using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Agents.Tools;

public sealed record ToolContext(Guid UserId, ExecutionMode Mode, ILlmProvider Llm, AgentRun Run);

public sealed record ToolResult<T>(bool Succeeded, T? Value, string Summary, string? Error = null)
{
    public static ToolResult<T> Ok(T value, string summary) => new(true, value, summary);
    public static ToolResult<T> Fail(string error) => new(false, default, error, error);
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    /// <summary>Tools that produce external side effects. They may only run from ExecuteApprovedActionCommand, never from agents.</summary>
    bool IsHighRisk { get; }
}

public interface IAgentTool<TInput, TOutput> : IAgentTool
{
    Task<ToolResult<TOutput>> ExecuteAsync(TInput input, ToolContext context, CancellationToken cancellationToken);
}

public sealed class ToolRegistry(IEnumerable<IAgentTool> tools)
{
    private readonly Dictionary<string, IAgentTool> byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public IReadOnlyCollection<IAgentTool> All => byName.Values;

    public IAgentTool<TInput, TOutput> Get<TInput, TOutput>(string name)
    {
        if (!byName.TryGetValue(name, out var tool)) throw new KeyNotFoundException($"Tool '{name}' is not registered.");
        if (tool.IsHighRisk) throw new InvalidOperationException($"Tool '{name}' is high-risk and cannot be invoked by an agent.");
        return tool as IAgentTool<TInput, TOutput> ?? throw new InvalidOperationException($"Tool '{name}' has an unexpected signature.");
    }
}

/// <summary>Executes a tool, records the call on the run, and never throws for tool-level failures (including HTTP timeouts).</summary>
public static class ToolInvoker
{
    public static async Task<(ToolResult<TOutput> Result, AgentRun Run)> InvokeAsync<TInput, TOutput>(
        IAgentTool<TInput, TOutput> tool, TInput input, ToolContext context, CancellationToken cancellationToken, ILogger? logger = null)
    {
        var watch = Stopwatch.StartNew();
        ToolResult<TOutput> result;
        Exception? failure = null;
        try
        {
            result = await tool.ExecuteAsync(input, context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failure = ex;
            result = ToolResult<TOutput>.Fail(ex.Message);
        }

        watch.Stop();
        var run = context.Run.WithToolCall(new AgentToolCall(tool.Name, Summarize(input), result.Summary, result.Succeeded, DateTimeOffset.UtcNow, watch.ElapsedMilliseconds));
        if (logger is not null)
        {
            if (result.Succeeded)
            {
                logger.LogInformation("Tool {Tool} succeeded in {DurationMs} ms (run {RunId}): {Summary}", tool.Name, watch.ElapsedMilliseconds, context.Run.Id, result.Summary);
            }
            else
            {
                logger.LogWarning(failure, "Tool {Tool} failed in {DurationMs} ms (run {RunId}): {Error}", tool.Name, watch.ElapsedMilliseconds, context.Run.Id, result.Error);
            }
        }

        return (result, run);
    }

    private static string Summarize<T>(T value)
    {
        var text = value is string s ? s : JsonSerializer.Serialize(value);
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}

internal static class LlmJson
{
    // Real models sometimes return numbers as strings ("85") or with decimals (85.0); accept both.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public static T Parse<T>(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        var json = start >= 0 && end > start ? content[start..(end + 1)] : content;
        return JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidOperationException("LLM returned an empty object.");
    }
}
