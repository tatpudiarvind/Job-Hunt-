using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class CandidateFactService(IRepository<CandidateFact> repository, AuditService audit, ILogger<CandidateFactService> logger)
{
    public Task<IReadOnlyList<CandidateFact>> ListAsync(CancellationToken cancellationToken) => repository.ListAsync(cancellationToken);

    public async Task<IReadOnlyList<CandidateFact>> ListVerifiedAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Where(f => f.IsVerified).ToList();

    public async Task<CandidateFact> AddAsync(string factType, string name, string value, string? sourceReference, Guid userId, CancellationToken cancellationToken)
    {
        var fact = CandidateFact.Create(factType, name, value, sourceReference ?? "");
        await repository.UpsertAsync(fact, cancellationToken);
        logger.LogInformation("Candidate fact {FactId} created: {FactType} / {FactName}", fact.Id, fact.FactType, fact.Name);
        await audit.RecordAsync(userId, "FACT_CREATED", "CandidateFact", fact.Id.ToString(), "SUCCESS", $"{fact.FactType}:{fact.Name}", cancellationToken);
        return fact;
    }

    public async Task<CandidateFact?> SetVerificationAsync(Guid id, bool verified, Guid userId, CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            logger.LogWarning("Candidate fact {FactId} could not be {Operation} because it was not found", id, verified ? "verified" : "invalidated");
            return null;
        }
        var updated = existing.SetVerification(verified);
        await repository.UpsertAsync(updated, cancellationToken);
        logger.LogInformation("Candidate fact {FactId} marked as {State}", id, verified ? "verified" : "unverified");
        await audit.RecordAsync(userId, verified ? "FACT_VERIFIED" : "FACT_INVALIDATED", "CandidateFact", id.ToString(), "SUCCESS", null, cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            logger.LogInformation("Candidate fact {FactId} deleted", id);
            await audit.RecordAsync(userId, "FACT_DELETED", "CandidateFact", id.ToString(), "SUCCESS", null, cancellationToken);
        }
        else
        {
            logger.LogWarning("Candidate fact {FactId} delete requested but the fact was not found", id);
        }
        return deleted;
    }
}
