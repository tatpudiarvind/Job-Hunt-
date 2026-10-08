using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

/// <summary>
/// Lets the Angular app report unexpected browser errors so they land in the same Markdown log as server events.
/// Anonymous (errors also happen on the login page) but rate limited, size capped, and only ever logged.
/// </summary>
public static class ClientLogEndpoints
{
    public const string Category = "ArvindJobHunter.Web.Client";
    private const int MaxMessageLength = 2_000;
    private const int MaxUrlLength = 500;
    private const int MaxStackLength = 8_000;

    public static IEndpointRouteBuilder MapClientLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/client-logs", (ClientLogRequest request, ILoggerFactory loggers) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["Message is required."] });
            }

            var level = request.Level?.Trim().ToLowerInvariant() switch
            {
                "error" => LogLevel.Error,
                "info" or "information" => LogLevel.Information,
                _ => LogLevel.Warning
            };
            var message = Truncate(request.Message.Trim(), MaxMessageLength);
            var page = string.IsNullOrWhiteSpace(request.Url) ? "unknown page" : Truncate(request.Url.Trim(), MaxUrlLength);
            var stack = string.IsNullOrWhiteSpace(request.Stack) ? null : new BrowserError(message, Truncate(request.Stack.Trim(), MaxStackLength));
            loggers.CreateLogger(Category).Log(level, stack, "Browser {Level} on {Page}: {Message}", level == LogLevel.Information ? "info" : level.ToString().ToLowerInvariant(), page, message);
            return Results.NoContent();
        })
        .WithTags("Diagnostics")
        .AllowAnonymous()
        .RequireRateLimiting("client-logs");

        return endpoints;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    /// <summary>Carries the browser's stack trace so the Markdown log shows it in a collapsible block.</summary>
    private sealed class BrowserError(string message, string stack) : Exception(message)
    {
        public override string StackTrace => stack;

        public override string ToString() => $"Browser error: {Message}\n{stack}";
    }
}
