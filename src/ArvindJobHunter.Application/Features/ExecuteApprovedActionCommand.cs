using System.Diagnostics;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

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
    IRepository<Job> jobs,
    IResumeDocumentService resumeDocuments,
    IGmailClient gmail,
    ApplicationService applications,
    AuditService audit,
    ILogger<ExecuteApprovedActionCommand> logger,
    IRepository<ApplicationPackage>? packages = null,
    IDocumentExporter? exporter = null,
    ICoverLetterDocumentWriter? coverLetters = null,
    IRepository<SubmissionPackage>? submissions = null,
    IBrowserApplyClient? browser = null)
{
    // Executions never overlap. Otherwise two clicks (or two tabs) with different idempotency keys could both
    // pass the guard before either one consumes the approval, and an email would be sent twice.
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    public async Task<IReadOnlyList<ExecutionReceipt>> ListAsync(CancellationToken cancellationToken) =>
        (await receipts.ListAsync(cancellationToken)).OrderByDescending(r => r.ExecutedAt).ToList();

    public async Task<ExecutionReceipt> ExecuteAsync(Guid approvalId, string idempotencyKey, Guid userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        await ExecutionGate.WaitAsync(cancellationToken);
        try
        {
            var existing = (await receipts.ListAsync(cancellationToken)).FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                logger.LogInformation("Execution of approval {ApprovalId} replayed: idempotency key already used, returning receipt {ReceiptId} ({Result})",
                    approvalId, existing.Id, existing.Result);
                return existing;
            }

            var approval = await approvals.GetAsync(approvalId, cancellationToken)
                ?? throw new KeyNotFoundException($"Approval {approvalId} was not found.");
            var current = await settings.GetAsync(cancellationToken);
            logger.LogInformation("Executing approval {ApprovalId}: {Action} on {TargetType} {TargetId} in {Mode} mode",
                approval.Id, approval.ActionType, approval.TargetType, approval.TargetId, current.Mode);
            var watch = Stopwatch.StartNew();

            ExecutionReceipt receipt;
            try
            {
                receipt = approval.ActionType switch
                {
                    ApprovalActionType.APPLY_RESUME_CHANGES => await ApplyResumeAsync(approval, idempotencyKey, current, userId, cancellationToken),
                    ApprovalActionType.CREATE_EMAIL_DRAFT => await EmailAsync(approval, idempotencyKey, current, send: false, userId, cancellationToken),
                    ApprovalActionType.SEND_EMAIL => await EmailAsync(approval, idempotencyKey, current, send: true, userId, cancellationToken),
                    ApprovalActionType.GENERATE_COVER_LETTER => await CoverLetterDocumentAsync(approval, idempotencyKey, current, cancellationToken),
                    ApprovalActionType.SUBMIT_APPLICATION => await SubmitApplicationAsync(approval, idempotencyKey, current, userId, cancellationToken),
                    _ => throw new ApprovalViolationException($"{approval.ActionType} has no executor in this build.")
                };
            }
            catch (ApprovalViolationException ex)
            {
                logger.LogWarning("Execution of approval {ApprovalId} blocked by the guard: {Reason}", approvalId, ex.Message);
                await audit.RecordAsync(userId, "EXECUTION_BLOCKED", "ApprovalRequest", approvalId.ToString(), "BLOCKED", ex.Message, CancellationToken.None);
                throw;
            }
            catch (Exception ex) when (ex is not KeyNotFoundException && (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested))
            {
                // Record a receipt instead of surfacing a bare 500: the approval stays usable and the failure is audited.
                logger.LogError(ex, "Execution of approval {ApprovalId} ({Action}) failed", approvalId, approval.ActionType);
                receipt = Receipt(approval, idempotencyKey, current.Mode, ExecutionResult.FAILED, null, null, $"Execution failed: {ex.Message}");
            }

            // The outcome must be recorded even if the browser went away meanwhile; otherwise a sent email would leave
            // the approval reusable.
            await receipts.UpsertAsync(receipt, CancellationToken.None);
            if (receipt.Result != ExecutionResult.FAILED) await approvals.ConsumeAsync(approval, CancellationToken.None);
            await audit.RecordAsync(userId, "EXECUTION_" + receipt.Result, "ApprovalRequest", approvalId.ToString(), receipt.Result.ToString(), receipt.Message, CancellationToken.None);
            logger.Log(receipt.Result == ExecutionResult.SUCCESS ? LogLevel.Information : LogLevel.Warning,
                "Approval {ApprovalId} finished with {Result} in {ElapsedMs} ms (receipt {ReceiptId})", approvalId, receipt.Result, watch.ElapsedMilliseconds, receipt.Id);
            return receipt;
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    private async Task<ExecutionReceipt> ApplyResumeAsync(ApprovalRequest approval, string key, RuntimeSettings current, Guid userId, CancellationToken cancellationToken)
    {
        var version = await resumes.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Resume version not found.");
        guard.EnsureAllowed(approval, ApprovalActionType.APPLY_RESUME_CHANGES, ApprovalRequest.ComputeHash(version.PayloadForApproval()), current.Mode);

        var websiteUrl = await GetJobUrlAsync(version.JobId, cancellationToken);
        if (current.Mode != ExecutionMode.LIVE)
        {
            await resumes.UpsertAsync(version.MarkApproved(), cancellationToken);
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, websiteUrl, $"{current.Mode} mode: resume changes approved and recorded; no document was written.");
        }

        if (string.IsNullOrWhiteSpace(version.MasterPath) || !File.Exists(version.MasterPath))
        {
            logger.LogWarning("LIVE resume execution for approval {ApprovalId} skipped: master resume {MasterPath} not found",
                approval.Id, string.IsNullOrWhiteSpace(version.MasterPath) ? "(not configured)" : version.MasterPath);
            return Receipt(approval, key, current.Mode, ExecutionResult.FAILED, null, websiteUrl,
                "LIVE mode: the master resume this version was prepared from was not found, so no document was written. Configure the master resume in Settings, then prepare the application again.");
        }

        var outputDirectory = Path.Combine(Path.GetDirectoryName(version.MasterPath)!, "tailored");
        var outputPath = Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(version.MasterPath)}-{version.JobId:N}-{version.Id:N}.docx");
        var report = await resumeDocuments.ApplyChangesAsync(version.MasterPath, outputPath, version.Changes, cancellationToken);
        var generated = version.MarkGenerated(report.OutputPath);
        var pdfNote = "";
        if (exporter?.AvailableEngine is not null)
        {
            try
            {
                var pdf = await exporter.ExportPdfAsync(report.OutputPath, Path.ChangeExtension(report.OutputPath, ".pdf"), cancellationToken);
                generated = generated.WithPdf(pdf.PdfPath, pdf.Sha256);
                pdfNote = $" PDF exported with {pdf.Engine} (sha256 {pdf.Sha256[..12]}).";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The .docx is the approved artifact; a PDF failure must not hide it.
                logger.LogWarning(ex, "PDF export failed for resume version {VersionId}", version.Id);
                pdfNote = $" PDF export failed: {ex.Message}";
            }
        }
        await resumes.UpsertAsync(generated, cancellationToken);

        var application = await applications.GetByJobAsync(version.JobId, cancellationToken);
        if (application is not null)
        {
            await applications.TryAdvanceAsync(application.WithResume(version.Id), ApplicationStatus.RESUME_PREPARED, "Tailored resume generated.", userId, cancellationToken);
        }

        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, report.OutputPath, websiteUrl, $"Applied {report.Applied.Count} change(s); skipped {report.Skipped.Count}.{pdfNote}");
    }

    private async Task<ExecutionReceipt> CoverLetterDocumentAsync(ApprovalRequest approval, string key, RuntimeSettings current, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Cover letter not found.");
        guard.EnsureAllowed(approval, ApprovalActionType.GENERATE_COVER_LETTER, ApprovalRequest.ComputeHash(draft.DocumentPayloadForApproval()), current.Mode);
        var websiteUrl = draft.JobId is { } jobId ? await GetJobUrlAsync(jobId, cancellationToken) : null;
        if (current.Mode != ExecutionMode.LIVE)
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, websiteUrl, $"{current.Mode} mode: cover letter approved; no document was written.");
        if (coverLetters is null)
            return Receipt(approval, key, current.Mode, ExecutionResult.FAILED, null, websiteUrl, "Cover letter documents are not available in this build.");

        var baseDirectory = draft.TemplatePath is { Length: > 0 } t ? Path.GetDirectoryName(t)! : Path.GetDirectoryName(current.MasterResumePath) is { Length: > 0 } d ? d : Path.GetTempPath();
        var outputPath = Path.Combine(baseDirectory, "tailored", $"cover-letter-{draft.JobId:N}-{draft.Id:N}.docx");
        await coverLetters.WriteAsync(draft.TemplatePath, outputPath, draft.Body, cancellationToken);
        string? pdfPath = null;
        var note = "";
        if (exporter?.AvailableEngine is not null)
        {
            try { pdfPath = (await exporter.ExportPdfAsync(outputPath, Path.ChangeExtension(outputPath, ".pdf"), cancellationToken)).PdfPath; note = " PDF exported."; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "PDF export failed for cover letter {DraftId}", draft.Id);
                note = $" PDF export failed: {ex.Message}";
            }
        }
        await drafts.UpsertAsync(draft.WithDocument(outputPath, pdfPath), cancellationToken);
        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, outputPath, websiteUrl, $"Cover letter written.{note}");
    }

    private async Task<ExecutionReceipt> EmailAsync(ApprovalRequest approval, string key, RuntimeSettings current, bool send, Guid userId, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Email draft not found.");
        var action = send ? ApprovalActionType.SEND_EMAIL : ApprovalActionType.CREATE_EMAIL_DRAFT;
        guard.EnsureAllowed(approval, action, ApprovalRequest.ComputeHash(draft.PayloadForApproval()), current.Mode);
        if (send) await EnsurePackageNotStaleAsync(draft, cancellationToken);

        var websiteUrl = draft.JobId is { } jobId ? await GetJobUrlAsync(jobId, cancellationToken) : null;
        if (current.Mode != ExecutionMode.LIVE)
        {
            await drafts.UpsertAsync(draft.MarkApproved(), cancellationToken);
            if (send && draft.JobId is { } simulatedJobId)
            {
                await MarkOutreachContactedAsync(draft, simulatedJobId, userId, cancellationToken, $"{current.Mode} mode: recruiter outreach recorded locally.");
            }
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, websiteUrl, $"{current.Mode} mode: { (send ? DryRunSendMessage(draft.Kind) : "email approved and recorded; Gmail was not contacted.") }");
        }

        // Point of no return: once the request goes to Gmail the outcome must be recorded, so the browser request's
        // cancellation token is no longer used, and any exception is UNKNOWN (UNKNOWN consumes the approval, so a
        // blind retry cannot send the email twice).
        GmailResult result;
        try
        {
            result = send
                ? await gmail.SendAsync(draft.To, draft.Subject, draft.Body, CancellationToken.None)
                : await gmail.CreateDraftAsync(draft.To, draft.Subject, draft.Body, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Gmail {Operation} for draft {DraftId} threw; the outcome is unknown", send ? "send" : "draft creation", draft.Id);
            return Receipt(approval, key, current.Mode, ExecutionResult.UNKNOWN, null, websiteUrl,
                $"Gmail call failed ({ex.GetType().Name}: {ex.Message}). The {(send ? "email may or may not have been sent" : "draft may or may not have been created")}; check Gmail before requesting a new approval.");
        }

        if (!result.Succeeded)
        {
            // Gmail rejected the request, so nothing was sent and the approval stays usable for a retry.
            await drafts.UpsertAsync(draft.MarkFailed(), CancellationToken.None);
            return Receipt(approval, key, current.Mode, ExecutionResult.FAILED, null, websiteUrl, result.Error);
        }

        var message = send ? LiveSendMessage(draft.Kind) : "Draft created in Gmail.";
        try
        {
            await drafts.UpsertAsync(send ? draft.MarkSent(result.ExternalId!) : draft.MarkCreatedInGmail(result.ExternalId!), CancellationToken.None);
            if (send && draft.JobId is { } sentJobId)
            {
                await MarkOutreachContactedAsync(draft, sentJobId, userId, CancellationToken.None, "Recruiter outreach sent.");
            }
        }
        catch (Exception ex)
        {
            // The message already left Gmail: a local bookkeeping failure must not turn this into a reusable approval.
            logger.LogError(ex, "Gmail {Operation} for draft {DraftId} succeeded, but updating local records failed", send ? "send" : "draft creation", draft.Id);
            message += $" Updating the local draft or application afterwards failed: {ex.Message}";
        }

        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, result.ExternalId, websiteUrl, message);
    }

    /// <summary>Once a package is frozen, content must not drift before sending; re-freeze to acknowledge changes.</summary>
    private async Task EnsurePackageNotStaleAsync(EmailDraft draft, CancellationToken cancellationToken)
    {
        if (packages is null || draft.JobId is not { } jobId) return;
        var application = await applications.GetByJobAsync(jobId, cancellationToken);
        if (application is null) return;
        var latest = (await packages.ListAsync(cancellationToken)).Where(p => p.ApplicationId == application.Id).MaxBy(p => p.Version);
        if (latest is null) return;
        var job = await jobs.GetAsync(jobId, cancellationToken);
        if (job is null) return;
        var resume = application.ResumeVersionId is { } r ? await resumes.GetAsync(r, cancellationToken) : null;
        var cover = application.CoverLetterDraftId is { } c ? await drafts.GetAsync(c, cancellationToken) : null;
        if (ApplicationPackageService.IsStale(latest, job, resume, cover))
            throw new ApprovalViolationException($"Application package v{latest.Version} is out of date: the job, resume or cover letter changed after it was frozen. Review and freeze it again before sending.");
    }

    private async Task<string?> GetJobUrlAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken);
        return string.IsNullOrWhiteSpace(job?.Url) ? null : job.Url;
    }

    private static bool IsRecruiter(string kind) => string.Equals(kind, "RECRUITER", StringComparison.OrdinalIgnoreCase);

    private static string DryRunSendMessage(string kind) =>
        IsRecruiter(kind) ? "recruiter outreach recorded locally; Gmail was not contacted." : "email approved and recorded; Gmail was not contacted.";

    private static string LiveSendMessage(string kind) =>
        IsRecruiter(kind) ? "Recruiter outreach sent via Gmail." : "Email sent via Gmail.";

    private async Task MarkOutreachContactedAsync(EmailDraft draft, Guid jobId, Guid userId, CancellationToken cancellationToken, string reason)
    {
        if (!IsRecruiter(draft.Kind))
        {
            return;
        }

        var application = await applications.GetByJobAsync(jobId, cancellationToken);
        if (application is not null)
        {
            await applications.TryAdvanceAsync(application, ApplicationStatus.RECRUITER_CONTACTED, reason, userId, cancellationToken);
        }
    }

    private async Task<ExecutionReceipt> SubmitApplicationAsync(ApprovalRequest approval, string key, RuntimeSettings current, Guid userId, CancellationToken cancellationToken)
    {
        if (submissions is null) throw new ApprovalViolationException("Browser submission is not available in this build.");
        var package = await submissions.GetAsync(approval.TargetId, cancellationToken)
            ?? throw new KeyNotFoundException("Submission package not found.");
        var payloadHash = ApprovalRequest.ComputeHash(package.PayloadForApproval());
        guard.EnsureAllowed(approval, ApprovalActionType.SUBMIT_APPLICATION, payloadHash, current.Mode);
        if (package.Status == SubmissionStatus.SUBMITTED) throw new ApprovalViolationException("This application was already submitted.");
        if (!package.ReadyForApproval) throw new ApprovalViolationException("Some required fields are still unanswered.");

        if (current.Mode != ExecutionMode.LIVE)
            return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, null, package.Url, $"{current.Mode} mode: submission approved; the form was not submitted.");

        var sites = current.Sites.Count > 0 ? current.Sites : SiteSettings.Defaults();
        ExternalActionGuard.EnsureSiteAllowed(package.Adapter, sites, SiteSettings.SubmittedToday(await submissions.ListAsync(cancellationToken), package.Adapter, DateTimeOffset.UtcNow));
        if (!File.Exists(package.ResumeFilePath) || ApplicationPackageService.FileHash(package.ResumeFilePath) != package.ResumeSha256)
            throw new ApprovalViolationException("The resume file changed or is missing since the form was prepared. Prepare again.");
        if (package.CoverLetterFilePath is not null &&
            (!File.Exists(package.CoverLetterFilePath) || ApplicationPackageService.FileHash(package.CoverLetterFilePath) != package.CoverLetterSha256))
            throw new ApprovalViolationException("The cover letter file changed or is missing since the form was prepared. Prepare again.");
        if (browser is null || package.SessionId is null)
            return Receipt(approval, key, current.Mode, ExecutionResult.FAILED, null, package.Url, "The browser worker is not available.");

        var result = await browser.SubmitAsync(new ArvindJobHunter.Contracts.Api.SubmitRequest(package.SessionId, payloadHash, key), cancellationToken);
        if (!result.Submitted)
        {
            await submissions.UpsertAsync(package.MarkFailed(result.Error ?? "Not submitted."), cancellationToken);
            // UNKNOWN when the click may have happened: the user must check the browser before retrying.
            var outcome = result.Error?.Contains("was clicked", StringComparison.OrdinalIgnoreCase) == true ? ExecutionResult.UNKNOWN : ExecutionResult.FAILED;
            return Receipt(approval, key, current.Mode, outcome, result.ScreenshotPath, package.Url, result.Error);
        }

        await submissions.UpsertAsync(package.MarkSubmitted(result.ConfirmationText, result.ScreenshotPath), cancellationToken);
        var application = await applications.GetAsync(package.ApplicationId, cancellationToken);
        if (application is not null)
            await applications.TryAdvanceAsync(application, ApplicationStatus.APPLIED, $"Submitted via {package.Adapter}.", userId, cancellationToken);
        return Receipt(approval, key, current.Mode, ExecutionResult.SUCCESS, result.ScreenshotPath, package.Url, $"Submitted on {package.Adapter}: {result.ConfirmationText}");
    }

    private static ExecutionReceipt Receipt(ApprovalRequest approval, string key, ExecutionMode mode, ExecutionResult result, string? externalRef, string? websiteUrl, string? message) =>
        new(Guid.NewGuid(), approval.Id, key, approval.ActionType, mode, result, externalRef, websiteUrl, message, DateTimeOffset.UtcNow);
}
