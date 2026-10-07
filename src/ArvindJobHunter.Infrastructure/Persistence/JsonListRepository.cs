using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>Cached list repository backed by a single JSON document. Mutations update memory then persist atomically.</summary>
public sealed class JsonListRepository<T>(IJsonStore<List<T>> store, Func<T, Guid> idSelector) : IRepository<T>, ICacheInvalidatable
    where T : class
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private List<T>? cache;

    public void Invalidate() { gate.Wait(); try { cache = null; } finally { gate.Release(); } }

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return cache!.ToList(); }
        finally { gate.Release(); }
    }

    public async Task<T?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        try { return cache!.FirstOrDefault(item => idSelector(item) == id); }
        finally { gate.Release(); }
    }

    public async Task UpsertAsync(T item, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cache ??= await store.LoadAsync(cancellationToken);
            var id = idSelector(item);
            var index = cache.FindIndex(existing => idSelector(existing) == id);
            if (index >= 0) cache[index] = item; else cache.Add(item);
            await store.SaveAsync(cache, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cache ??= await store.LoadAsync(cancellationToken);
            var removed = cache.RemoveAll(existing => idSelector(existing) == id) > 0;
            if (removed) await store.SaveAsync(cache, cancellationToken);
            return removed;
        }
        finally { gate.Release(); }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (cache is not null) return;
        await gate.WaitAsync(cancellationToken);
        try { cache ??= await store.LoadAsync(cancellationToken); }
        finally { gate.Release(); }
    }
}
