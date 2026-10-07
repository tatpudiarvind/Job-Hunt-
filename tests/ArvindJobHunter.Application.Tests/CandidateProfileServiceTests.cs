using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Tests;

public sealed class CandidateProfileServiceTests
{
    [Fact]
    public async Task UpdateAsync_CreatesProfileWhenItDoesNotExist()
    {
        var repository = new InMemoryCandidateProfileRepository();
        var service = new CandidateProfileService(repository);
        var id = Guid.NewGuid();

        var profile = await service.UpdateAsync(
            id,
            "Arvind Tatpudi",
            "Senior Software Engineer",
            "Siemens Healthineers",
            CancellationToken.None);

        Assert.Equal(id, profile.Id);
        Assert.Equal("Arvind Tatpudi", profile.Name);
        Assert.Equal("Senior Software Engineer", profile.CurrentRole);
        Assert.Equal("Siemens Healthineers", profile.CurrentCompany);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesExistingProfileWithoutChangingIdentity()
    {
        var repository = new InMemoryCandidateProfileRepository();
        var service = new CandidateProfileService(repository);
        var id = Guid.NewGuid();

        await service.UpdateAsync(id, "Arvind Tatpudi", "Senior Software Engineer", "Siemens Healthineers", CancellationToken.None);
        var updated = await service.UpdateAsync(id, "Arvind Tatpudi", "AI Software Engineer", null, CancellationToken.None);

        Assert.Equal(id, updated.Id);
        Assert.Equal("AI Software Engineer", updated.CurrentRole);
        Assert.Null(updated.CurrentCompany);
        Assert.Single(repository.Profiles);
    }

    private sealed class InMemoryCandidateProfileRepository : ICandidateProfileRepository
    {
        public List<CandidateProfile> Profiles { get; } = [];

        public Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.SingleOrDefault(profile => profile.Id == id));

        public Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken)
        {
            if (!Profiles.Contains(profile))
            {
                Profiles.Add(profile);
            }

            return Task.CompletedTask;
        }
    }
}
