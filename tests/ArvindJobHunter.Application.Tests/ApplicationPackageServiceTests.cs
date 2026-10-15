using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArvindJobHunter.Application.Tests;

public sealed class ApplicationPackageServiceTests
{
    private readonly InMemoryRepository<ApplicationPackage> packages = new(p => p.Id);
    private readonly InMemoryRepository<JobApplication> applications = new(a => a.Id);
    private readonly InMemoryRepository<Job> jobs = new(j => j.Id);
    private readonly InMemoryRepository<ResumeVersion> resumes = new(r => r.Id);
    private readonly InMemoryRepository<EmailDraft> emails = new(e => e.Id);
    private readonly InMemoryAuditStore auditStore = new();
    private readonly Guid userId = Guid.NewGuid();

    private ApplicationPackageService Service() => new(packages, applications, jobs, resumes, emails,
        new AuditService(auditStore, NullLogger<AuditService>.Instance), NullLogger<ApplicationPackageService>.Instance);

    private async Task<(JobApplication Application, Job Job, ResumeVersion Resume, EmailDraft Cover)> SeedAsync(string coverBody = "I would love to join Contoso.")
    {
        var job = Job.Create(".NET Engineer", "Contoso", "Berlin, Germany", "MANUAL", null, "C# role");
        var resume = ResumeVersion.Propose(job.Id, "master.docx", [new ResumeChange("Summary", "a", "b", ["fact"])]).MarkApproved().MarkGenerated("out.docx");
        var cover = EmailDraft.Create(job.Id, "COVER_LETTER", "", "Application", coverBody);
        var application = JobApplication.Create(job.Id).WithResume(resume.Id).WithCoverLetter(cover.Id);
        await jobs.UpsertAsync(job, CancellationToken.None);
        await resumes.UpsertAsync(resume, CancellationToken.None);
        await emails.UpsertAsync(cover, CancellationToken.None);
        await applications.UpsertAsync(application, CancellationToken.None);
        return (application, job, resume, cover);
    }

    [Fact]
    public async Task CreatePackage_FreezesHashes_AndIsIdempotentUntilContentChanges()
    {
        var seeded = await SeedAsync();
        var service = Service();

        var first = await service.CreatePackageAsync(seeded.Application.Id, userId, CancellationToken.None);
        var again = await service.CreatePackageAsync(seeded.Application.Id, userId, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(1, first!.Version);
        Assert.Equal(first.Id, again!.Id);

        await emails.UpsertAsync(seeded.Cover.Edit("", "Application", "Updated letter for Contoso."), CancellationToken.None);
        var review = await service.ReviewAsync(seeded.Application.Id, CancellationToken.None);
        Assert.False(review!.PackageIsCurrent);

        var second = await service.CreatePackageAsync(seeded.Application.Id, userId, CancellationToken.None);
        Assert.Equal(2, second!.Version);
        Assert.NotEqual(first.CoverLetterHash, second.CoverLetterHash);
        Assert.Equal(first.ResumeHash, second.ResumeHash);
    }

    [Fact]
    public async Task CreatePackage_IsRefused_WhenCoverLetterNamesAnotherCompany()
    {
        var seeded = await SeedAsync("I would love to join Fabrikam.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().CreatePackageAsync(seeded.Application.Id, userId, CancellationToken.None));
        Assert.Contains("does not mention Contoso", ex.Message);
        Assert.Empty(await packages.ListAsync(CancellationToken.None));
    }

    [Fact]
    public void Consistency_FlagsResumeForAnotherJob_UnapprovedResume_AndExcludedSponsorship()
    {
        var job = Job.Create("Engineer", "Contoso", "Paris", "MANUAL", null, "role") with
        {
            Sponsorship = new SponsorshipEvidence(SponsorshipStatus.EXCLUDED, "no visa sponsorship", "MANUAL", DateTimeOffset.UtcNow)
        };
        var resume = ResumeVersion.Propose(Guid.NewGuid(), "m.docx", []);
        var issues = ApplicationPackageService.CheckConsistency(JobApplication.Create(job.Id), job, resume, null);

        Assert.Contains(issues, i => i.Contains("different job"));
        Assert.Contains(issues, i => i.Contains("not been approved"));
        Assert.Contains(issues, i => i.Contains("sponsorship is not available"));
    }

    [Fact]
    public void Sponsorship_IsUnknown_WhenPostingIsSilent_AndKeepsMatchedPhrase()
    {
        var silent = JobDiscoveryService.EvaluateSponsorship("Engineer", "C# and Azure", "Berlin", "Fake");
        var mentioned = JobDiscoveryService.EvaluateSponsorship("Engineer", "Visa sponsorship available.", "Berlin", "Fake");

        Assert.Equal(SponsorshipStatus.UNKNOWN, silent.Status);
        Assert.Null(silent.MatchedPhrase);
        Assert.Equal(SponsorshipStatus.MENTIONED, mentioned.Status);
        Assert.Equal("visa sponsorship", mentioned.MatchedPhrase);
        Assert.Equal("Fake", mentioned.Source);
    }

    [Fact]
    public void RecruiterContact_RequiresEmailAndSource_UnlessNotFound()
    {
        Assert.Throws<ArgumentException>(() => RecruiterContact.Create(RecruiterContactStatus.USER_PROVIDED, "Jane", null, "LinkedIn"));
        Assert.Throws<ArgumentException>(() => RecruiterContact.Create(RecruiterContactStatus.VERIFIED, "Jane", "jane@contoso.com", " "));

        var notFound = RecruiterContact.Create(RecruiterContactStatus.NOT_FOUND, null, "guess@contoso.com", null);
        Assert.Null(notFound.Email);
        Assert.False(notFound.IsUsable);

        var provided = RecruiterContact.Create(RecruiterContactStatus.USER_PROVIDED, "Jane", " jane@contoso.com ", "Company careers page");
        Assert.True(provided.IsUsable);
        Assert.Equal("jane@contoso.com", provided.Email);
    }

    [Theory]
    [InlineData("Contoso GmbH", "I would love to join Contoso.", true)]
    [InlineData("Fabrikam Ltd.", "Dear Fabrikam team", true)]
    [InlineData("Contoso GmbH", "I would love to join Fabrikam.", false)]
    public void CompanyMatch_IgnoresLegalSuffixes(string company, string body, bool expected) =>
        Assert.Equal(expected, ApplicationPackageService.MentionsCompany(EmailDraft.Create(null, "COVER_LETTER", "", "Application", body), company));

    [Fact]
    public async Task Review_BackfillsSponsorshipEvidence_ForJobsSavedBeforeItExisted()
    {
        var seeded = await SeedAsync();
        Assert.Null(seeded.Job.Sponsorship);

        var review = await Service().ReviewAsync(seeded.Application.Id, CancellationToken.None);

        Assert.Equal(SponsorshipStatus.UNKNOWN, review!.Job.Sponsorship!.Status);
        Assert.NotNull((await jobs.GetAsync(seeded.Job.Id, CancellationToken.None))!.Sponsorship);
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
}
