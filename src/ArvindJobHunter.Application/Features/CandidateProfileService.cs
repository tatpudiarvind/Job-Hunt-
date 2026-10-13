using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class CandidateProfileService(ICandidateProfileRepository repository, ILogger<CandidateProfileService> logger)
{
    public static readonly Guid LocalCandidateId = Domain.LocalUser.CandidateId;

    public Task<CandidateProfile?> GetCurrentAsync(CancellationToken cancellationToken) =>
        GetAsync(LocalCandidateId, cancellationToken);

    public Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        repository.GetAsync(id, cancellationToken);

    public async Task<CandidateProfile> UpdateAsync(
        Guid id,
        string name,
        string currentRole,
        string? currentCompany,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetAsync(id, cancellationToken);
        var profile = existing ?? new CandidateProfile(id, name, currentRole, currentCompany);

        profile.Update(name, currentRole, currentCompany);
        await repository.SaveAsync(profile, cancellationToken);
        logger.LogInformation("Candidate profile {CandidateId} {Operation}: role={CurrentRole}, company={CurrentCompany}",
            id,
            existing is null ? "created" : "updated",
            currentRole,
            string.IsNullOrWhiteSpace(currentCompany) ? "(none)" : currentCompany);
        return profile;
    }
}
