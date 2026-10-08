using System.Diagnostics;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

/// <summary>
/// Persists audit events and mirrors each one into the application log. Events carry the current request's
/// correlation id, so an entry on the Activity page can be found in the Markdown log and vice versa.
/// </summary>
public sealed class AuditService(IAuditStore store, ILogger<AuditService> logger)
{
    private static readonly HashSet<string> ProblemOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BLOCKED", "FAILED", "UNKNOWN", "ERROR", "RESTORED_FROM_BACKUP", "RESET_TO_EMPTY"
    };

    public async Task RecordAsync(Guid userId, string action, string targetType, string targetId, string outcome, string? details = null, CancellationToken cancellationToken = default)
    {
        await store.AddAsync(AuditEvent.Create(userId, action, targetType, targetId, outcome, details, CurrentCorrelationId()), cancellationToken);
        logger.Log(ProblemOutcomes.Contains(outcome) ? LogLevel.Warning : LogLevel.Information,
            "Audit {Action} · {TargetType} {TargetId} → {Outcome}{Details}",
            action, targetType, targetId, outcome, string.IsNullOrWhiteSpace(details) ? "" : $" · {details}");
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).OrderByDescending(e => e.At).ToList();

    private static string? CurrentCorrelationId() => Activity.Current is { } activity
        ? activity.IdFormat == ActivityIdFormat.W3C ? activity.TraceId.ToHexString() : activity.RootId
        : null;
}
