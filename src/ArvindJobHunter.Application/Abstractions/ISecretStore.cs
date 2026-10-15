namespace ArvindJobHunter.Application.Abstractions;

public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken cancellationToken);
    Task SaveAsync(string name, string? value, CancellationToken cancellationToken);
}
