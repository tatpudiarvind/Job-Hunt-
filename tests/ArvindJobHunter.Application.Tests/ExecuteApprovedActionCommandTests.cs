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
    private readonly InMemoryAuditStore auditStore = new();
    private readonly RecordingResumeDocuments documents = new();
    private readonly FakeGmail gmail = new();

    public ExecuteApprovedActionCommandTests() => File.WriteAllBytes(masterResume, [1, 2, 3]);

    public void Dispose() => File.Delete(masterResume);

    private ExecuteApprovedActionCommand Command(ExecutionMode mode)
    {
        var settings = new FixedSettings(new RuntimeSettings { Mode = mode });
        var audit = new AuditService(auditStore, NullLogger<AuditService>.Instance);
        return new ExecuteApprovedActionCommand(
            new ApprovalService(approvals, settings, audit, NullLogger<ApprovalService>.Instance),
            new ExternalActionGuard(), settings, receipts, resumes, drafts, documents, gmail,
            new ApplicationService(applications, audit), audit, NullLogger<ExecuteApprovedActionCommand>.Instance);
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

    private async Task<ApprovalRequest> SendApprovalAsync()
    {
        var draft = EmailDraft.Create(null, "RECRUITER", "recruiter@example.com", "Hello", "Body");
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
