namespace ArvindJobHunter.Domain.Entities;

public sealed record AuditEvent(
    Guid Id,
    Guid UserId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string? Details,
    string? CorrelationId,
    DateTimeOffset At)
{
    public static AuditEvent Create(Guid userId, string action, string targetType, string targetId, string outcome, string? details = null, string? correlationId = null) =>
        new(Guid.NewGuid(), userId, action, targetType, targetId, outcome, details, correlationId, DateTimeOffset.UtcNow);
}
