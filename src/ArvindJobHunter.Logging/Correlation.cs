using System.Diagnostics;

namespace ArvindJobHunter.Logging;

/// <summary>
/// The correlation id that ties together a request's Markdown log lines, its <c>X-Correlation-Id</c>
/// response header and the audit events it produced: the W3C trace id of the current activity.
/// </summary>
public static class Correlation
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ScopeKey = "CorrelationId";

    public static string? CurrentId()
    {
        var activity = Activity.Current;
        if (activity is null) return null;
        return activity.IdFormat == ActivityIdFormat.W3C ? activity.TraceId.ToHexString() : activity.RootId;
    }
}
