using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArvindJobHunter.Application.Tests;

public sealed class ExecuteApprovedActionCommandTests : IDisposable
{
    private readonly string masterResume = Path.Combine(Path.GetTempPath(), $"ajh-master-{Guid.NewGuid():N}.docx");
    private readonly InMemoryRepository<ApprovalRequest> approvals = new(a => a.Id);
    private readonly InMemoryRepository<ExecutionReceipt> receipts = new(r => r.Id);
    private readonly InMemoryRepository<ResumeVersion> resumes = new(r => r.Id);
    private readonly InMemoryRepository<EmailDraft> drafts = new(d => d.Id);
    private readonly InMemoryRepository<JobApplication> applications = new(a => a.Id);
    private readonly InMemoryRepository<ApplicationPackage> packages = new(p => p.Id);
    private readonly InMemoryRepository<Job> jobs = new(j => j.Id);
    private readonly InMemoryAuditStore auditStore = new();
    private readonly RecordingResumeDocuments documents = new();
    private readonly FakeGmail gmail = new();
    private readonly InMemoryRepository<SubmissionPackage> submissions = new(s => s.Id);
    private readonly FakeBrowser browser = new();

    public ExecuteApprovedActionCommandTests() => File.WriteAllBytes(masterResume, [1, 2, 3]);

    public void Dispose() => File.Delete(masterResume);

    private ExecuteApprovedActionCommand Command(ExecutionMode mode, IReadOnlyList<SiteSettings>? sites = null)
    {
        var settings = new FixedSettings(new RuntimeSettings { Mode = mode, Sites = sites ?? [] });
        var audit = new AuditService(auditStore, NullLogger<AuditService>.Instance);
        return new ExecuteApprovedActionCommand(
            new ApprovalService(approvals, settings, audit, NullLogger<ApprovalService>.Instance),
            new ExternalActionGuard(), settings, receipts, resumes, drafts, jobs, documents, gmail,
            new ApplicationService(applications, audit, NullLogger<ApplicationService>.Instance), audit, NullLogger<ExecuteApprovedActionCommand>.Instance, packages,
            submissions: submissions, browser: browser);
    }

    private async Task<(ApprovalRequest Approval, SubmissionPackage Package)> SubmissionApprovalAsync(ExecutionMode mode, string adapter = "company")
    {
        var now = DateTimeOffset.UtcNow;
        var package = new SubmissionPackage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "https://jobs.example.com/1", adapter, masterResume,
            ApplicationPackageService.FileHash(masterResume), null, [new SubmissionField("Email", "a@b.com", true, "profile")], [], null,
            SubmissionStatus.AWAITING_APPROVAL, null, now, now, "session-1");
        await submissions.UpsertAsync(package, CancellationToken.None);
        return (await ApprovedAsync(ApprovalActionType.SUBMIT_APPLICATION, package.Id, package.PayloadForApproval(), mode), package);
    }

    [Theory]
    [InlineData(ExecutionMode.DEMO)]
    [InlineData(ExecutionMode.DRY_RUN)]
    public async Task Submit_InNonLiveModes_NeverClicksSubmit(ExecutionMode mode)
    {
        var (approval, _) = await SubmissionApprovalAsync(mode);

        var receipt = await Command(mode).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Equal(0, browser.Submits);
    }

    [Fact]
    public async Task Submit_Live_ClicksSubmitAndMarksPackageSubmitted()
    {
        var (approval, package) = await SubmissionApprovalAsync(ExecutionMode.LIVE);

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Equal(1, browser.Submits);
        Assert.Equal(SubmissionStatus.SUBMITTED, (await submissions.GetAsync(package.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Submit_Live_OnDisabledSite_IsBlocked()
    {
        var (approval, _) = await SubmissionApprovalAsync(ExecutionMode.LIVE, "linkedin");

        await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None));
        Assert.Equal(0, browser.Submits);
    }

    [Fact]
    public async Task Submit_Live_WhenAnswersChangedAfterApproval_IsBlocked()
    {
        var (approval, package) = await SubmissionApprovalAsync(ExecutionMode.LIVE);
        await submissions.UpsertAsync(package.Answer(new Dictionary<string, string> { ["Email"] = "other@b.com" }), CancellationToken.None);

        await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None));
        Assert.Equal(0, browser.Submits);
    }

    [Fact]
    public async Task Submit_Live_WhenResumeFileChanged_IsBlocked()
    {
        var (approval, _) = await SubmissionApprovalAsync(ExecutionMode.LIVE);
        File.WriteAllBytes(masterResume, [9, 9, 9]);

        await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None));
        Assert.Equal(0, browser.Submits);
    }

    [Fact]
    public async Task Submit_Live_WhenCoverLetterFileChanged_IsBlocked()
    {
        var cover = Path.Combine(Path.GetTempPath(), $"ajh-cover-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(cover, [4, 5, 6]);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var package = new SubmissionPackage(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "https://jobs.example.com/1", "company", masterResume,
                ApplicationPackageService.FileHash(masterResume), null, [new SubmissionField("Email", "a@b.com", true, "profile")], [], null,
                SubmissionStatus.AWAITING_APPROVAL, null, now, now, "session-1", null, cover, ApplicationPackageService.FileHash(cover));
            await submissions.UpsertAsync(package, CancellationToken.None);
            var approval = await ApprovedAsync(ApprovalActionType.SUBMIT_APPLICATION, package.Id, package.PayloadForApproval(), ExecutionMode.LIVE);
            File.WriteAllBytes(cover, [7, 7, 7]);

            await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "s1", LocalUser.Id, CancellationToken.None));
            Assert.Equal(0, browser.Submits);
        }
        finally { File.Delete(cover); }
    }

    private sealed class FakeBrowser : IBrowserApplyClient
    {
        public int Submits { get; private set; }
        public Task<ArvindJobHunter.Contracts.Api.FillPreviewResponse> PrepareAsync(ArvindJobHunter.Contracts.Api.FillPreviewRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BindAsync(string sessionId, string payloadHash, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ArvindJobHunter.Contracts.Api.SubmitResponse> SubmitAsync(ArvindJobHunter.Contracts.Api.SubmitRequest request, CancellationToken cancellationToken)
        {
            Submits++;
            return Task.FromResult(new ArvindJobHunter.Contracts.Api.SubmitResponse(true, "Thank you for applying", null));
        }
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<DownloadedFile>> DownloadsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DownloadedFile>>([]);
        public Task<CapturedJobPage?> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult<CapturedJobPage?>(null);
        public Task<PageLinks?> LinksAsync(CancellationToken cancellationToken) => Task.FromResult<PageLinks?>(null);
    }

    private async Task<ApprovalRequest> ApprovedAsync(ApprovalActionType action, Guid targetId, string payload, ExecutionMode mode)
    {
        var approval = ApprovalRequest.Create(action, "Target", targetId, payload, "summary", mode, LocalUser.Id, TimeSpan.FromHours(1))
            .Decide(true, LocalUser.Id, null, DateTimeOffset.UtcNow);
        await approvals.UpsertAsync(approval, CancellationToken.None);
        return approval;
    }

    private async Task<ApprovalRequest> ResumeApprovalAsync(ExecutionMode mode, string masterPath)
    {
        var version = ResumeVersion.Propose(Guid.NewGuid(), masterPath, [new ResumeChange("SUMMARY", "old", "new", [])]);
        await resumes.UpsertAsync(version, CancellationToken.None);
        return await ApprovedAsync(ApprovalActionType.APPLY_RESUME_CHANGES, version.Id, version.PayloadForApproval(), mode);
    }

    private async Task<ApprovalRequest> SendApprovalAsync(string kind = "RECRUITER", Guid? jobId = null)
    {
        var draft = EmailDraft.Create(jobId, kind, "recruiter@example.com", "Hello", "Body");
        await drafts.UpsertAsync(draft, CancellationToken.None);
        return await ApprovedAsync(ApprovalActionType.SEND_EMAIL, draft.Id, draft.PayloadForApproval(), ExecutionMode.LIVE);
    }

    private async Task<ApprovalStatus> StatusOf(ApprovalRequest approval) => (await approvals.GetAsync(approval.Id, CancellationToken.None))!.Status;

    [Fact]
    public async Task DryRun_RecordsTheApproval_WithoutWritingADocument()
    {
        var approval = await ResumeApprovalAsync(ExecutionMode.DRY_RUN, masterResume);

        var receipt = await Command(ExecutionMode.DRY_RUN).ExecuteAsync(approval.Id, "k1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.StartsWith("DRY_RUN mode", receipt.Message);
        Assert.Equal(0, documents.Applies);
        Assert.Equal(ApprovalStatus.CONSUMED, await StatusOf(approval));
    }

    [Fact]
    public async Task Live_WithAMissingMasterResume_FailsWithoutConsumingTheApproval()
    {
        var approval = await ResumeApprovalAsync(ExecutionMode.LIVE, Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.docx"));

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "k1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.FAILED, receipt.Result);
        Assert.Contains("master resume", receipt.Message);
        Assert.Equal(0, documents.Applies);
        Assert.Equal(ApprovalStatus.APPROVED, await StatusOf(approval));
        Assert.Contains(auditStore.Events, e => e.Action == "EXECUTION_FAILED");
    }

    [Fact]
    public async Task Live_WithAnExistingMaster_WritesTheTailoredCopy()
    {
        var approval = await ResumeApprovalAsync(ExecutionMode.LIVE, masterResume);

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "k1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Equal(1, documents.Applies);
        Assert.Equal(ApprovalStatus.CONSUMED, await StatusOf(approval));
    }

    [Fact]
    public async Task UnexpectedExecutorFailure_BecomesAFailedReceipt_InsteadOfA500()
    {
        documents.Failure = new IOException("disk full");
        var approval = await ResumeApprovalAsync(ExecutionMode.LIVE, masterResume);

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "k1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.FAILED, receipt.Result);
        Assert.Contains("disk full", receipt.Message);
        Assert.Equal(ApprovalStatus.APPROVED, await StatusOf(approval));
        Assert.Single(await receipts.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GmailException_IsUnknown_AndConsumesTheApprovalSoItCannotBeBlindlyRetried()
    {
        gmail.Failure = new HttpRequestException("connection reset");
        var approval = await SendApprovalAsync();

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "k1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.UNKNOWN, receipt.Result);
        Assert.Contains("may or may not have been sent", receipt.Message);
        Assert.Equal(ApprovalStatus.CONSUMED, await StatusOf(approval));
    }

    [Fact]
    public async Task ConcurrentExecutions_WithDifferentKeys_SendTheEmailOnlyOnce()
    {
        gmail.Delay = TimeSpan.FromMilliseconds(200);
        var approval = await SendApprovalAsync();

        var first = Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-1", LocalUser.Id, CancellationToken.None);
        var second = Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-2", LocalUser.Id, CancellationToken.None);
        var outcomes = await Task.WhenAll(Capture(first), Capture(second));

        Assert.Equal(1, gmail.Sends);
        Assert.Single(outcomes, o => o.Receipt?.Result == ExecutionResult.SUCCESS);
        Assert.Single(outcomes, o => o.Error is ApprovalViolationException);
    }

    [Fact]
    public async Task ReplayingAnIdempotencyKey_ReturnsTheOriginalReceipt()
    {
        var approval = await SendApprovalAsync();
        var command = Command(ExecutionMode.LIVE);

        var first = await command.ExecuteAsync(approval.Id, "same-key", LocalUser.Id, CancellationToken.None);
        var replay = await command.ExecuteAsync(approval.Id, "same-key", LocalUser.Id, CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, gmail.Sends);
    }

    [Fact]
    public async Task BrowserAbortAfterGmailAcceptedTheEmail_StillRecordsTheSend_AndBlocksARetry()
    {
        var approval = await SendApprovalAsync();
        using var request = new CancellationTokenSource();
        gmail.OnSend = request.Cancel;

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-1", LocalUser.Id, request.Token);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Equal(ApprovalStatus.CONSUMED, await StatusOf(approval));
        await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-2", LocalUser.Id, CancellationToken.None));
        Assert.Equal(1, gmail.Sends);
    }

    [Fact]
    public async Task SendingACoverLetterEmail_DoesNotMarkTheApplicationAsApplied()
    {
        var jobId = Guid.NewGuid();
        var application = JobApplication.Create(jobId)
            .TransitionTo(ApplicationStatus.MATCHED, "match")
            .TransitionTo(ApplicationStatus.SHORTLISTED, "shortlist")
            .TransitionTo(ApplicationStatus.RESUME_PREPARED, "resume")
            .TransitionTo(ApplicationStatus.AWAITING_APPROVAL, "approval")
            .TransitionTo(ApplicationStatus.APPROVED, "approved");
        await applications.UpsertAsync(application, CancellationToken.None);
        var approval = await SendApprovalAsync("COVER_LETTER", jobId);

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "send-cover-letter", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Equal("Email sent via Gmail.", receipt.Message);
        var unchanged = await applications.GetAsync(application.Id, CancellationToken.None);
        Assert.Equal(ApplicationStatus.APPROVED, unchanged!.Status);
    }

    [Fact]
    public async Task Sending_IsBlocked_WhenTheFrozenPackageNoLongerMatchesTheContent()
    {
        var job = Job.Create("Engineer", "Contoso", "Berlin", "MANUAL", null, "C# role");
        await jobs.UpsertAsync(job, CancellationToken.None);
        var cover = EmailDraft.Create(job.Id, "COVER_LETTER", "", "Application", "Hello Contoso.");
        await drafts.UpsertAsync(cover, CancellationToken.None);
        var application = JobApplication.Create(job.Id).WithCoverLetter(cover.Id);
        await applications.UpsertAsync(application, CancellationToken.None);
        await packages.UpsertAsync(new ApplicationPackage(Guid.NewGuid(), application.Id, job.Id, 1, "", null, null, cover.Id, null,
            ApplicationPackageService.PackageHash(job, null, cover), LocalUser.Id, DateTimeOffset.UtcNow), CancellationToken.None);
        await drafts.UpsertAsync(cover.Edit("", "Application", "Edited after freezing, Contoso."), CancellationToken.None);
        var approval = await SendApprovalAsync("RECRUITER", job.Id);

        var ex = await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "stale", LocalUser.Id, CancellationToken.None));

        Assert.Contains("out of date", ex.Message);
        Assert.Equal(0, gmail.Sends);
        Assert.Equal(ApprovalStatus.APPROVED, await StatusOf(approval));
    }

    [Fact]
    public async Task BookkeepingFailureAfterASuccessfulSend_StillConsumesTheApproval()
    {
        var approval = await SendApprovalAsync();
        drafts.FailUpsertWhen = d => d.Status == EmailDraftStatus.SENT;

        var receipt = await Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-1", LocalUser.Id, CancellationToken.None);

        Assert.Equal(ExecutionResult.SUCCESS, receipt.Result);
        Assert.Contains("Updating the local draft or application afterwards failed", receipt.Message);
        Assert.Equal(ApprovalStatus.CONSUMED, await StatusOf(approval));
        await Assert.ThrowsAsync<ApprovalViolationException>(() => Command(ExecutionMode.LIVE).ExecuteAsync(approval.Id, "click-2", LocalUser.Id, CancellationToken.None));
        Assert.Equal(1, gmail.Sends);
    }

    private static async Task<(ExecutionReceipt? Receipt, Exception? Error)> Capture(Task<ExecutionReceipt> execution)
    {
        try { return (await execution, null); }
        catch (Exception ex) { return (null, ex); }
    }

    private sealed class InMemoryRepository<T>(Func<T, Guid> id) : IRepository<T> where T : class
    {
        private readonly Lock sync = new();
        private readonly List<T> items = [];

        /// <summary>Simulates a disk failure for matching items (e.g. once the JSON store's retries are exhausted).</summary>
        public Func<T, bool>? FailUpsertWhen { get; set; }

        public Task<IReadOnlyList<T>> ListAsync(CancellationToken cancellationToken) { lock (sync) return Task.FromResult<IReadOnlyList<T>>(items.ToList()); }

        public Task<T?> GetAsync(Guid key, CancellationToken cancellationToken) { lock (sync) return Task.FromResult(items.FirstOrDefault(i => id(i) == key)); }

        public Task UpsertAsync(T item, CancellationToken cancellationToken)
        {
            if (FailUpsertWhen?.Invoke(item) == true) throw new IOException("disk full");
            lock (sync)
            {
                items.RemoveAll(i => id(i) == id(item));
                items.Add(item);
            }

            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid key, CancellationToken cancellationToken) { lock (sync) return Task.FromResult(items.RemoveAll(i => id(i) == key) > 0); }
    }

    private sealed class InMemoryAuditStore : IAuditStore
    {
        public List<AuditEvent> Events { get; } = [];

        public Task AddAsync(AuditEvent entry, CancellationToken cancellationToken) { lock (Events) Events.Add(entry); return Task.CompletedTask; }

        public Task<IReadOnlyList<AuditEvent>> ListAsync(CancellationToken cancellationToken) { lock (Events) return Task.FromResult<IReadOnlyList<AuditEvent>>(Events.ToList()); }
    }

    private sealed class FixedSettings(RuntimeSettings settings) : IRuntimeSettingsProvider
    {
        public Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task SaveAsync(RuntimeSettings value, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingResumeDocuments : IResumeDocumentService
    {
        public int Applies { get; private set; }
        public Exception? Failure { get; set; }

        public Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(new ResumeDocument([]));

        public Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Applies++;
            return Task.FromResult(new ResumeChangeReport(outputPath, changes, []));
        }
    }

    private sealed class FakeGmail : IGmailClient
    {
        private int sends;
        public int Sends => sends;
        public Exception? Failure { get; set; }
        public TimeSpan Delay { get; set; }

        /// <summary>Runs after Gmail "accepted" the message, e.g. to simulate the browser aborting the request.</summary>
        public Action? OnSend { get; set; }

        public Task<GmailResult> CreateDraftAsync(string to, string subject, string body, CancellationToken cancellationToken) => SendAsync(to, subject, body, cancellationToken);

        public async Task<GmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            if (Failure is not null) throw Failure;
            Interlocked.Increment(ref sends);
            OnSend?.Invoke();
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return new GmailResult(true, $"gmail-{sends}", null);
        }

        public Task<IReadOnlyList<GmailLabel>> ListLabelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GmailLabel>>(Array.Empty<GmailLabel>());

        public Task<GmailSearchResults> ListMessagesAsync(int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0));

        public Task<GmailSearchResults> SearchMessagesAsync(string query, int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0));

        public Task<GmailMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken) =>
            Task.FromResult<GmailMessage?>(null);
    }
}

public sealed class EmailRecipientValidationTests
{
    [Theory]
    [InlineData("recruiter@example.com", true)]
    [InlineData("Jane Recruiter <jane@example.com>", true)]
    [InlineData("a@example.com, b@example.com", true)]
    [InlineData("a@example.com; b@example.com", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-an-address", false)]
    [InlineData("a@example.com\r\nBcc: everyone@example.com", false)]
    public void HasValidRecipients(string to, bool expected) => Assert.Equal(expected, EmailDraftService.HasValidRecipients(to));
}

