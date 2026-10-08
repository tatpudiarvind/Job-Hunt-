using System.Collections;
using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging;

/// <summary>
/// Logs one line per HTTP request (method, path, status, duration) and stamps every response with an
/// <c>X-Correlation-Id</c> header matching the correlation column of the Markdown log and the audit trail.
/// Register it first so it also observes responses produced by the exception handler.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Correlation.CurrentId() ?? context.TraceIdentifier;
        context.Response.OnStarting(static state =>
        {
            var (httpContext, id) = ((HttpContext, string))state;
            httpContext.Response.Headers[Correlation.HeaderName] = id;
            return Task.CompletedTask;
        }, (context, correlationId));

        using var scope = logger.BeginScope(new CorrelationScope(correlationId));
        var started = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        catch (Exception ex) when (LogFailure(context, started, ex))
        {
            throw; // never reached: LogFailure only observes the exception
        }

        LogCompleted(context, started);
    }

    private bool LogFailure(HttpContext context, long started, Exception exception)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("{Method} {Path} cancelled by the client after {ElapsedMs} ms", context.Request.Method, Target(context.Request), Elapsed(started));
        }
        else
        {
            logger.LogError(exception, "{Method} {Path} failed after {ElapsedMs} ms with an unhandled {ExceptionType}",
                context.Request.Method, Target(context.Request), Elapsed(started), exception.GetType().Name);
        }

        return false;
    }

    private void LogCompleted(HttpContext context, long started)
    {
        var request = context.Request;
        var status = context.Response.StatusCode;
        var aborted = context.RequestAborted.IsCancellationRequested;
        var level = HttpMethods.IsOptions(request.Method) || request.Path.StartsWithSegments("/health") ? LogLevel.Debug
            : status >= 500 ? LogLevel.Error
            : status >= 400 && !aborted ? LogLevel.Warning
            : LogLevel.Information;
        if (!logger.IsEnabled(level)) return;

        logger.Log(level, "{Method} {Path} → {StatusCode} in {ElapsedMs} ms{Note}",
            request.Method, Target(request), status, Elapsed(started), aborted ? " (client disconnected)" : "");
    }

    private static string Target(HttpRequest request) => request.Path.Value + request.QueryString.Value;

    private static string Elapsed(long started) =>
        Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);

    private sealed class CorrelationScope(string id) : IReadOnlyList<KeyValuePair<string, object>>
    {
        public int Count => 1;

        public KeyValuePair<string, object> this[int index] =>
            index == 0 ? new(Correlation.ScopeKey, id) : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{Correlation.ScopeKey}:{id}";
    }
}
