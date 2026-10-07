using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class CandidateFactService(IRepository<CandidateFact> repository, AuditService audit)
{
    public Task<IReadOnlyList<CandidateFact>> ListAsync(CancellationToken cancellationToken) => repository.ListAsync(cancellationToken);

    public async Task<IReadOnlyList<CandidateFact>> ListVerifiedAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Where(f => f.IsVerified).ToList();

    public async Task<CandidateFact> AddAsync(string factType, string name, string value, string? sourceReference, Guid userId, CancellationToken cancellationToken)
    {
        var fact = CandidateFact.Create(factType, name, value, sourceReference ?? "");
        await repository.UpsertAsync(fact, cancellationToken);
        await audit.RecordAsync(userId, "FACT_CREATED", "CandidateFact", fact.Id.ToString(), "SUCCESS", $"{fact.FactType}:{fact.Name}", cancellationToken);
        return fact;
    }

    public async Task<CandidateFact?> SetVerificationAsync(Guid id, bool verified, Guid userId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        if (existing is null) return null;
        var updated = existing.SetVerification(verified);
        await repository.UpsertAsync(updated, cancellationToken);
        await audit.RecordAsync(userId, verified ? "FACT_VERIFIED" : "FACT_INVALIDATED", "CandidateFact", id.ToString(), "SUCCESS", null, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        if (deleted) await audit.RecordAsync(userId, "FACT_DELETED", "CandidateFact", id.ToString(), "SUCCESS", null, cancellationToken);
        return deleted;
    }
}
