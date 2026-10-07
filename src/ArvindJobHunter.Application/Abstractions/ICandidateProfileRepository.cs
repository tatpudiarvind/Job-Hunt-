using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

public interface ICandidateProfileRepository
{
    Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken);
}
