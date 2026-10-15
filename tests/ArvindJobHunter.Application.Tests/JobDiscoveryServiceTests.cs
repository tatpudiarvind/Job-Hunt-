using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArvindJobHunter.Application.Tests;

public sealed class JobDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverAsync_ImportsOnlySelectedCountryJobsWithVisaSponsorship()
    {
        var jobs = new InMemoryRepository<Job>(j => j.Id);
        var applications = new InMemoryRepository<JobApplication>(a => a.Id);
        var facts = new InMemoryRepository<CandidateFact>(f => f.Id);
        var auditStore = new InMemoryAuditStore();
        var audit = new AuditService(auditStore, NullLogger<AuditService>.Instance);
        var jobService = new JobService(jobs, applications, audit, NullLogger<JobService>.Instance);
        var profileService = new CandidateProfileService(new InMemoryCandidateProfileRepository(
            new CandidateProfile(CandidateProfileService.LocalCandidateId, "Arvind", "Senior .NET Engineer", null)),
            NullLogger<CandidateProfileService>.Instance);
        var factService = new CandidateFactService(facts, audit, NullLogger<CandidateFactService>.Instance);

        await facts.UpsertAsync(CandidateFact.Create("SKILL", "C#", "C#", "seed").SetVerification(true), CancellationToken.None);
        await facts.UpsertAsync(CandidateFact.Create("SKILL", "Azure", "Azure", "seed").SetVerification(true), CancellationToken.None);
        await facts.UpsertAsync(CandidateFact.Create("SKILL", ".NET", ".NET", "seed").SetVerification(true), CancellationToken.None);
        await facts.UpsertAsync(CandidateFact.Create("PREFERENCE", "Remote", "Remote", "seed").SetVerification(true), CancellationToken.None);

        var source = new FakeSource(
        [
            new DiscoveredJobPosting("1", "Senior .NET Engineer", "Contoso UK", "London, United Kingdom", "Fake", "https://example.com/1", "C#, Azure. Visa sponsorship available. Remote within UK.", []),
            new DiscoveredJobPosting("2", "Senior .NET Engineer", "Contoso DE", "Berlin, Germany", "Fake", "https://example.com/2", "C#, Azure. Visa sponsorship available. Hybrid in Germany.", []),
            new DiscoveredJobPosting("3", "Platform Engineer", "Fabrikam US", "Austin, United States", "Fake", "https://example.com/3", "Azure and Kubernetes. Visa sponsorship available.", [])
        ]);

        var service = new JobDiscoveryService([source], profileService, factService, new StubJobSearchPreferencesProvider(new JobSearchPreferences
        {
            PreferredCountries = ["Germany"],
            PreferredRoles = ["Senior .NET Engineer"],
            WorkMode = "Hybrid"
        }), jobService, audit, NullLogger<JobDiscoveryService>.Instance);

        var result = await service.DiscoverAsync(Guid.NewGuid(), 20, CancellationToken.None);

        var imported = Assert.Single(result.Jobs);
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Skipped);
        Assert.Equal("Contoso DE", imported.Company);
        Assert.Contains("Germany", imported.Location);
        Assert.Contains(auditStore.Events, e => e.Action == "JOB_DISCOVERY_COMPLETED");
    }

    [Theory]
    [InlineData("C#, .NET, Azure and SQL Server. Visa sponsorship available.", 100)]
    [InlineData("C#, .NET, Azure, Kafka and Kubernetes. Visa sponsorship available.", 60)]
    [InlineData("Python and Django only.", 0)]
    public void SkillCoveragePercent_MeasuresShareOfRequiredSkillsOnResume(string description, int expected)
    {
        var posting = new DiscoveredJobPosting("1", "Backend Engineer", "Contoso", "Berlin, Germany", "Fake", "https://example.com/1", description, []);
        Assert.Equal(expected, JobDiscoveryService.SkillCoveragePercent(posting, ["C#", ".NET", "Azure", "SQL Server"]));
    }

    private sealed class FakeSource(IReadOnlyList<DiscoveredJobPosting> jobs) : IJobDiscoverySource
    {
        public string Name => "Fake";
        public Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken) => Task.FromResult(jobs);
    }

    private sealed class StubJobSearchPreferencesProvider(JobSearchPreferences current) : IJobSearchPreferencesProvider
    {
        public Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken) => Task.FromResult(current);
        public Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryRepository<T>(Func<T, Guid> id) : IRepository<T> where T : class
    {
        private readonly List<T> items = [];
        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<T>>(items.ToList());
        public Task<T?> GetAsync(Guid key, CancellationToken cancellationToken) => Task.FromResult(items.FirstOrDefault(i => id(i) == key));
        public Task UpsertAsync(T item, CancellationToken cancellationToken)
        {
            items.RemoveAll(i => id(i) == id(item));
            items.Add(item);
            return Task.CompletedTask;
        }
        public Task<bool> DeleteAsync(Guid key, CancellationToken cancellationToken) => Task.FromResult(items.RemoveAll(i => id(i) == key) > 0);
    }

    private sealed class InMemoryAuditStore : IAuditStore
    {
        public List<AuditEvent> Events { get; } = [];
        public Task AddAsync(AuditEvent entry, CancellationToken cancellationToken) { Events.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEvent>>(Events);
    }

    private sealed class InMemoryCandidateProfileRepository(CandidateProfile profile) : ICandidateProfileRepository
    {
        private CandidateProfile current = profile;
        public Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(current.Id == id ? current : null);
        public Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken) { current = profile; return Task.CompletedTask; }
    }
}
