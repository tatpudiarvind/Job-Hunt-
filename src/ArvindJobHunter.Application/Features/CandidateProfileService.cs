using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class CandidateProfileService(ICandidateProfileRepository repository)
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
        var profile = await repository.GetAsync(id, cancellationToken)
            ?? new CandidateProfile(id, name, currentRole, currentCompany);

        profile.Update(name, currentRole, currentCompany);
        await repository.SaveAsync(profile, cancellationToken);
        return profile;
    }
}
