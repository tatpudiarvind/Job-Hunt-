using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArvindJobHunter.Application.Tests;

public sealed class ResumeAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAndSaveAsync_StoresResumeLinkedInUrl_WithoutOverwritingManualPreference()
    {
        var facts = new InMemoryRepository<CandidateFact>(f => f.Id);
        var profileRepository = new InMemoryCandidateProfileRepository();
        var profileService = new CandidateProfileService(profileRepository, NullLogger<CandidateProfileService>.Instance);
        var preferenceProvider = new InMemoryJobSearchPreferencesProvider(new JobSearchPreferences
        {
            LinkedInProfileUrl = "https://www.linkedin.com/in/manual-profile/"
        });
        var audit = new AuditService(new InMemoryAuditStore(), NullLogger<AuditService>.Instance);
        var service = new ResumeAnalysisService(
            new StubResumeDocumentService(new ResumeDocument([
                new ResumeParagraph(0, "Arvind Tatpudi", null),
                new ResumeParagraph(1, "Senior Backend Engineer", null),
                new ResumeParagraph(2, "LinkedIn: https://www.linkedin.com/in/arvind-tatpudi-071137120/", null),
                new ResumeParagraph(3, "Skills", null),
                new ResumeParagraph(4, "C#, Azure, Microservices", null)
            ])),
            new ThrowingLlmResolver(),
            profileService,
            facts,
            preferenceProvider,
            audit,
            NullLogger<ResumeAnalysisService>.Instance);

        await service.AnalyzeAndSaveAsync("resume.pdf", Guid.NewGuid(), CancellationToken.None);

        var saved = await preferenceProvider.GetAsync(CancellationToken.None);
        Assert.Equal("https://www.linkedin.com/in/manual-profile/", saved.LinkedInProfileUrl);
        Assert.Equal("https://www.linkedin.com/in/arvind-tatpudi-071137120/", saved.ResumeLinkedInProfileUrl);
        Assert.Equal("https://www.linkedin.com/in/manual-profile/", saved.EffectiveLinkedInProfileUrl);
    }

    private sealed class StubResumeDocumentService(ResumeDocument document) : IResumeDocumentService
    {
        public Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(document);
        public Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingLlmResolver : ILlmProviderResolver
    {
        public Task<ILlmProvider> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult<ILlmProvider>(new ThrowingLlmProvider());
    }

    private sealed class ThrowingLlmProvider : ILlmProvider
    {
        public string Name => "Test";
        public bool IsConfigured => true;
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }

    private sealed class InMemoryJobSearchPreferencesProvider(JobSearchPreferences current) : IJobSearchPreferencesProvider
    {
        private JobSearchPreferences value = current;
        public Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken) => Task.FromResult(value);
        public Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken) { value = preferences; return Task.CompletedTask; }
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
        public Task AddAsync(AuditEvent entry, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEvent>>([]);
    }

    private sealed class InMemoryCandidateProfileRepository : ICandidateProfileRepository
    {
        private CandidateProfile? current;
        public Task<CandidateProfile?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(current?.Id == id ? current : null);
        public Task SaveAsync(CandidateProfile profile, CancellationToken cancellationToken) { current = profile; return Task.CompletedTask; }
    }
}
