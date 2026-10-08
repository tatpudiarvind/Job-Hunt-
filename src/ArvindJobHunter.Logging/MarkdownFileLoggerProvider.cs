using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArvindJobHunter.Logging;

/// <summary>
/// Writes every log entry the host's filters let through to a daily, human-readable Markdown file
/// (one table per run of entries, exceptions in collapsible blocks, secrets masked).
/// Filters are configured like any other provider under <c>Logging:MarkdownFile:LogLevel</c>.
/// </summary>
[ProviderAlias("MarkdownFile")]
public sealed class MarkdownFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentDictionary<string, MarkdownFileLogger> loggers = new(StringComparer.Ordinal);
    private readonly MarkdownLogWriter? writer;
    private readonly TimeProvider time;
    private readonly bool utc;
    private volatile bool disposed;

    public MarkdownFileLoggerProvider(IOptionsMonitor<MarkdownFileLoggerOptions> options, IHostEnvironment? environment = null, TimeProvider? timeProvider = null)
        : this(options.CurrentValue, environment, timeProvider)
    {
    }

    internal MarkdownFileLoggerProvider(MarkdownFileLoggerOptions options, IHostEnvironment? environment, TimeProvider? timeProvider)
    {
        time = timeProvider ?? TimeProvider.System;
        utc = options.UseUtcTimestamps;
        var contentRoot = environment?.ContentRootPath is { Length: > 0 } root ? root : AppContext.BaseDirectory;
        LogDirectory = ResolveDirectory(options.Directory, contentRoot);
        if (!options.Enabled) return;

        var effective = options.WithDirectory(LogDirectory);
        writer = new MarkdownLogWriter(effective, CreateSession(effective, environment, contentRoot, Now()), Now);
    }

    /// <summary>Absolute folder the log files are written to.</summary>
    public string LogDirectory { get; }

    /// <summary>The file currently being appended to, once the writer has opened it.</summary>
    public string? CurrentFilePath => writer?.CurrentFilePath;

    internal IExternalScopeProvider? ScopeProvider { get; private set; }

    internal bool IsWriting => writer is not null && !disposed;

    public ILogger CreateLogger(string categoryName) => loggers.GetOrAdd(categoryName, name => new MarkdownFileLogger(name, this));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => ScopeProvider = scopeProvider;

    /// <summary>Drains everything queued so far to disk and closes the file with a session summary.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        writer?.Dispose();
    }

    internal DateTimeOffset Now() => utc ? time.GetUtcNow() : time.GetLocalNow();

    internal void Write(in MarkdownLogEntry entry) => writer?.Enqueue(entry);

    internal string? CurrentCorrelationId()
    {
        var id = Correlation.CurrentId();
        if (id is not null || ScopeProvider is null) return id;

        string? fromScope = null;
        ScopeProvider.ForEachScope((scope, _) =>
        {
            if (fromScope is not null || scope is not IEnumerable<KeyValuePair<string, object?>> pairs) return;
            foreach (var pair in pairs)
            {
                if (pair.Key == Correlation.ScopeKey && pair.Value is not null)
                {
                    fromScope = pair.Value.ToString();
                    return;
                }
            }
        }, (object?)null);
        return fromScope;
    }

    internal static string ResolveDirectory(string? directory, string contentRoot)
    {
        if (string.IsNullOrWhiteSpace(directory)) return Path.GetFullPath(Path.Combine(contentRoot, "logs"));
        var expanded = Environment.ExpandEnvironmentVariables(directory.Trim());
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(contentRoot, expanded));
    }

    private static MarkdownSessionInfo CreateSession(MarkdownFileLoggerOptions options, IHostEnvironment? environment, string contentRoot, DateTimeOffset startedAt)
    {
        var entry = Assembly.GetEntryAssembly();
        var version = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entry?.GetName().Version?.ToString()
            ?? "unknown";
        return new MarkdownSessionInfo(
            options.Title,
            environment?.ApplicationName ?? entry?.GetName().Name ?? "application",
            version,
            environment?.EnvironmentName ?? "Production",
            Environment.MachineName,
            Environment.ProcessId,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            contentRoot,
            options.Directory,
            startedAt);
    }
}
