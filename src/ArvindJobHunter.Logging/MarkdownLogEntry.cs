using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging;

internal readonly record struct MarkdownLogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    string? CorrelationId);

internal sealed record MarkdownSessionInfo(
    string Title,
    string ApplicationName,
    string Version,
    string Environment,
    string Machine,
    int ProcessId,
    string Runtime,
    string OperatingSystem,
    string ContentRoot,
    string LogDirectory,
    DateTimeOffset StartedAt);
