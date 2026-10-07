using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

public interface IAuditStore
{
    Task AddAsync(AuditEvent entry, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken);
}
