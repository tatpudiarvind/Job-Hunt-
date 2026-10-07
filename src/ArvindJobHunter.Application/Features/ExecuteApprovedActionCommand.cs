using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

/// <summary>
/// The single code path through which approved side effects run. Every execution passes the
/// <see cref="IExternalActionGuard"/>, is idempotent by key, consumes the approval, and produces a receipt.
/// </summary>
public sealed class ExecuteApprovedActionCommand(
    ApprovalService approvals,
    IExternalActionGuard guard,
    IRuntimeSettingsProvider settings,
    IRepository<ExecutionReceipt> receipts,
    IRepository<ResumeVersion> resumes,
    IRepository<EmailDraft> drafts,
    IResumeDocumentService resumeDocuments,
    IGmailClient gmail,
    ApplicationService applications,
    AuditService audit)
{
    public async Task<IReadOnlyList<ExecutionReceipt>> ListAsync(CancellationToken cancellationToken) =>
        (await receipts.ListAsync(cancellationToken)).OrderByDescending(r => r.ExecutedAt).ToList();

    public async Task<ExecutionReceipt> ExecuteAsync(Guid approvalId, string idempotencyKey, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        var existing = (await receipts.ListAsync(cancellationToken)).FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;

        var approval = await approvals.GetAsync(approvalId, cancellationToken)
            ?? throw new KeyNotFoundException($"Approval {approvalId} was not found.");
        var current = await settings.GetAsync(cancellationToken);

        ExecutionReceipt receipt;
        try
        {
            receipt = approval.ActionType switch
            {
                ApprovalActionType.APPLY_RESUME_CHANGES => await ApplyResumeAsync(approval, idempotencyKey, current, userId, cancellationToken),
                ApprovalActionType.CREATE_EMAIL_DRAFT => await EmailAsync(approval, idempotencyKey, current, send: false, userId, cancellationToken),
                ApprovalActionType.SEND_EMAIL => await EmailAsync(approval, idempotencyKey, current, send: true, userId, cancellationToken),
                _ => throw new ApprovalViolationException($"{approval.ActionType} has no executor in this build.")
            };
        }
        catch (ApprovalViolationException ex)
        {
            await audit.RecordAsync(userId, "EXECUTION_BLOCKED", "ApprovalRequest", approvalId.ToString(), "BLOCKED", ex.Message, cancellationToken);
            throw;
        }

        await receipts.UpsertAsync(receipt, cancellationToken);
        if (receipt.Result != ExecutionResult.FAILED) await approvals.ConsumeAsync(approval, cancellationToken);
        await audit.RecordAsync(userId, "EXECUTION_" + receipt.Result, "ApprovalRequest", approvalId.ToString(), receipt.Result.ToString(), receipt.Message, cancellationToken);
        return receipt;
    }

    private async Task<ExecutionReceipt> ApplyResumeAsync(ApprovalRequest approval, string key, RuntimeSettings current, Guid userId, CancellationToken cancellationToken)
    {
        var version = await resumes.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Resume version not found.");
        guard.EnsureAllowed(approval, ApprovalActionType.APPLY_RESUME_CHANGES, ApprovalRequest.ComputeHash(version.PayloadForApproval()), current.Mode);

        if (current.Mode == ExecutionMode.DEMO || string.IsNullOrWhiteSpace(version.MasterPath) || !File.Exists(version.MasterPath))
        {
            await resumes.UpsertAsync(version.MarkApproved(), cancellationToken);
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, "Demo mode: resume changes approved and recorded; no document was written.");
        }

        var outputDirectory = Path.Combine(Path.GetDirectoryName(version.MasterPath)!, "tailored");
        var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(version.MasterPath)}-{version.JobId:N}-{version.Id:N}.docx");
        var report = await resumeDocuments.ApplyChangesAsync(version.MasterPath, outputPath, version.Changes, cancellationToken);
        await resumes.UpsertAsync(version.MarkGenerated(report.OutputPath), cancellationToken);

        var application = await applications.GetByJobAsync(version.JobId, cancellationToken);
        if (application is not null)
        {
            await applications.TryAdvanceAsync(application.WithResume(version.Id), ApplicationStatus.RESUME_PREPARED, "Tailored resume generated.", userId, cancellationToken);
        }

        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, report.OutputPath, $"Applied {report.Applied.Count} change(s); skipped {report.Skipped.Count}.");
    }

    private async Task<ExecutionReceipt> EmailAsync(ApprovalRequest approval, string key, RuntimeSettings current, bool send, Guid userId, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Email draft not found.");
        var action = send ? ApprovalActionType.SEND_EMAIL : ApprovalActionType.CREATE_EMAIL_DRAFT;
        guard.EnsureAllowed(approval, action, ApprovalRequest.ComputeHash(draft.PayloadForApproval()), current.Mode);

        if (current.Mode != ExecutionMode.LIVE)
        {
            await drafts.UpsertAsync(draft.MarkApproved(), cancellationToken);
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, $"{current.Mode} mode: email approved and recorded; Gmail was not contacted.");
        }

        var result = send
            ? await gmail.SendAsync(draft.To, draft.Subject, draft.Body, cancellationToken)
            : await gmail.CreateDraftAsync(draft.To, draft.Subject, draft.Body, cancellationToken);

        if (!result.Succeeded)
        {
            await drafts.UpsertAsync(draft.MarkFailed(), cancellationToken);
            return Receipt(approval, key, current.Mode, ExecutionResult.FAILED, null, result.Error);
        }

        await drafts.UpsertAsync(send ? draft.MarkSent(result.ExternalId!) : draft.MarkCreatedInGmail(result.ExternalId!), cancellationToken);
        if (draft.JobId is { } jobId && send)
        {
            var application = await applications.GetByJobAsync(jobId, cancellationToken);
            if (application is not null) await applications.TryAdvanceAsync(application, ApplicationStatus.RECRUITER_CONTACTED, "Email sent.", userId, cancellationToken);
        }

        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, result.ExternalId, send ? "Email sent via Gmail." : "Draft created in Gmail.");
    }

    private static ExecutionReceipt Receipt(ApprovalRequest approval, string key, ExecutionMode mode, ExecutionResult result, string? externalRef, string? message) =>
        new(Guid.NewGuid(), approval.Id, key, approval.ActionType, mode, result, externalRef, message, DateTimeOffset.UtcNow);
}
