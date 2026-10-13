namespace ArvindJobHunter.Application.Abstractions;

public interface IJsonStore<T>
    where T : class, new()
{
    Task<T> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(T value, CancellationToken cancellationToken);

    /// <summary>True once the document has been saved at least once (a missing file loads as <c>new T()</c>).</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken);
}

/// <summary>Cached, in-process repository for a list of identifiable aggregates persisted as one JSON file.</summary>
public interface IRepository<T>
    where T : class
{
    Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken);
    Task<T?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertAsync(T item, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
