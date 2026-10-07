using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class AuditService(IAuditStore store)
{
    public Task RecordAsync(Guid userId, string action, string targetType, string targetId, string outcome, string? details = null, CancellationToken cancellationToken = default) =>
        store.AddAsync(AuditEvent.Create(userId, action, targetType, targetId, outcome, details), cancellationToken);

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).OrderByDescending(e => e.At).ToList();
}
