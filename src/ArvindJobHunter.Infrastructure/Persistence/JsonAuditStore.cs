using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Infrastructure.Persistence;

public sealed class JsonAuditStore(IJsonStore<List<AuditEvent>> store) : IAuditStore, ICacheInvalidatable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<AuditEvent>? cache;

    public void Invalidate() { gate.Wait(); try { cache = null; } finally { gate.Release(); } }

    public async Task AddAsync(AuditEvent entry, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cache ??= await store.LoadAsync(cancellationToken);
            cache.Add(entry);
            await store.SaveAsync(cache, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cache ??= await store.LoadAsync(cancellationToken);
            return cache.ToList();
        }
        finally { gate.Release(); }
    }
}
