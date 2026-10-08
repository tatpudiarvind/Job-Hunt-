using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging;

internal sealed class MarkdownFileLogger(string category, MarkdownFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.ScopeProvider?.Push(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && provider.IsWriting;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        ArgumentNullException.ThrowIfNull(formatter);

        var message = formatter(state, exception) ?? string.Empty;
        if (message.Length == 0 && exception is null) return;
        provider.Write(new MarkdownLogEntry(provider.Now(), logLevel, category, message, exception?.ToString(), provider.CurrentCorrelationId()));
    }
}
