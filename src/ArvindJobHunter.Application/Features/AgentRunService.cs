using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class AgentRunService(IRepository<AgentRun> repository)
{
    public async Task<IReadOnlyList<AgentRun>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(r => r.StartedAt).ToList();

    public Task<AgentRun?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public Task SaveAsync(AgentRun run, CancellationToken cancellationToken) => repository.UpsertAsync(run, cancellationToken);
}
