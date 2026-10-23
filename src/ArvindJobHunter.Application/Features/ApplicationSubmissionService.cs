using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

/// <summary>
/// Prepares a browser submission: picks the resume, asks the Worker to fill (never submit) the form in the visible
/// browser, freezes the result as a <see cref="SubmissionPackage"/> and requests a SUBMIT_APPLICATION approval.
/// </summary>
public sealed class ApplicationSubmissionService(
    IRepository<SubmissionPackage> submissions,
    IRepository<Job> jobs,
    IRepository<ResumeVersion> resumes,
    IRepository<EmailDraft> emails,
    ApplicationService applications,
    ApprovalService approvals,
    IRuntimeSettingsProvider settings,
    IJobSearchPreferencesProvider preferences,
    CandidateProfileService profiles,
    CandidateFactService facts,
    AuditService audit,
    ILogger<ApplicationSubmissionService> logger,
    IBrowserApplyClient? browser = null)
{
    public async Task<IReadOnlyList<SubmissionPackage>> ListAsync(Guid? jobId, CancellationToken cancellationToken) =>
        (await submissions.ListAsync(cancellationToken)).Where(s => jobId is null || s.JobId == jobId).OrderByDescending(s => s.UpdatedAt).ToList();

    public Task<SubmissionPackage?> GetAsync(Guid id, CancellationToken cancellationToken) => submissions.GetAsync(id, cancellationToken);

    public async Task<SubmissionPackage> PrepareAsync(Guid jobId, string? adapter, Guid userId, CancellationToken cancellationToken)
    {
        if (browser is null) throw new InvalidOperationException("Browser auto-apply needs the Worker. Configure Worker:BaseUrl and Worker:SharedSecret.");
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        if (string.IsNullOrWhiteSpace(job.Url)) throw new InvalidOperationException("The job has no application URL.");
        var current = await settings.GetAsync(cancellationToken);

        var choice = ResumeSelector.Select(job, await resumes.ListAsync(cancellationToken), current.RoleResumePaths,
            current.ResumeLibrary.Where(e => !e.IsCoverLetter).Select(e => new ResumeCandidate(e.Path, e.Label, e.Keywords)).ToList(),
            current.MasterResumeFor(job.ResolveCountryCode()))
            ?? throw new InvalidOperationException("No resume file is available. Generate a tailored resume or upload a master resume.");

        var application = await applications.EnsureForJobAsync(jobId, cancellationToken);
        var cover = application.CoverLetterDraftId is { } coverId ? await emails.GetAsync(coverId, cancellationToken) : null;
        var answers = await AnswersAsync(cancellationToken);

        var coverFile = ResumeSelector.SelectCoverLetter(job,
            current.ResumeLibrary.Where(e => e.IsCoverLetter).Select(e => new ResumeCandidate(e.Path, e.Label, e.Keywords)).ToList());

        var preview = await browser.PrepareAsync(new FillPreviewRequest(job.Url, choice.Path, cover?.Body, answers, adapter, coverFile?.Path), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var coverUploaded = coverFile is not null && preview.Fields.Any(f => f.Source == "cover letter");
        var package = new SubmissionPackage(
            Guid.NewGuid(), jobId, application.Id, job.Url, preview.Adapter, choice.Path, ApplicationPackageService.FileHash(choice.Path), cover?.Body,
            preview.Fields.Select(f => new SubmissionField(f.Label, f.Value, f.Required, f.Source)).ToList(),
            preview.UnansweredFields, preview.ScreenshotPath,
            preview.UnansweredFields.Count == 0 && preview.Blocker is null ? SubmissionStatus.PREPARED : SubmissionStatus.NEEDS_ANSWERS,
            null, now, now, preview.SessionId, preview.Blocker,
            coverUploaded ? coverFile!.Path : null, coverUploaded ? ApplicationPackageService.FileHash(coverFile!.Path) : null);
        await submissions.UpsertAsync(package, cancellationToken);
        await audit.RecordAsync(userId, "SUBMISSION_PREPARED", "SubmissionPackage", package.Id.ToString(), package.Status.ToString(),
            $"{preview.Adapter} {new Uri(job.Url).Host}; resume: {choice.Reason}{(coverUploaded ? $"; {coverFile!.Reason}" : "")}", cancellationToken);
        logger.LogInformation("Submission {Id} prepared for job {JobId} via {Adapter}; {Missing} unanswered", package.Id, jobId, preview.Adapter, preview.UnansweredFields.Count);

        return preview.Blocker is null && package.ReadyForApproval ? await RequestApprovalAsync(package, userId, cancellationToken) : package;
    }

    /// <summary>Answering changes the payload, so any earlier approval is invalidated and a fresh one requested.</summary>
    public async Task<SubmissionPackage?> AnswerAsync(Guid id, IReadOnlyDictionary<string, string> answers, Guid userId, CancellationToken cancellationToken)
    {
        var package = await submissions.GetAsync(id, cancellationToken);
        if (package is null) return null;
        if (package.Status == SubmissionStatus.SUBMITTED) throw new InvalidOperationException("This application was already submitted.");
        await approvals.InvalidateForTargetAsync(id, "Submission answers changed.", userId, cancellationToken);
        var answered = package.Answer(answers);
        await submissions.UpsertAsync(answered, cancellationToken);
        await audit.RecordAsync(userId, "SUBMISSION_ANSWERED", "SubmissionPackage", id.ToString(), answered.Status.ToString(), string.Join(", ", answers.Keys), cancellationToken);
        return answered.ReadyForApproval ? await RequestApprovalAsync(answered, userId, cancellationToken) : answered;
    }

    private async Task<SubmissionPackage> RequestApprovalAsync(SubmissionPackage package, Guid userId, CancellationToken cancellationToken)
    {
        var payload = package.PayloadForApproval();
        var summary = $"Submit application on {package.Adapter} ({new Uri(package.Url).Host}) with {Path.GetFileName(package.ResumeFilePath)}; {package.Fields.Count} field(s) filled.";
        var approval = await approvals.RequestAsync(ApprovalActionType.SUBMIT_APPLICATION, "SubmissionPackage", package.Id, payload, summary, userId, cancellationToken);
        if (browser is not null && package.SessionId is not null)
            await browser.BindAsync(package.SessionId, ApprovalRequest.ComputeHash(payload), cancellationToken);
        var pending = package.WithApproval(approval.Id);
        await submissions.UpsertAsync(pending, cancellationToken);
        return pending;
    }

    /// <summary>Known answers: verified facts first, then profile and preferences. Nothing is invented.</summary>
    private async Task<IReadOnlyDictionary<string, string>> AnswersAsync(CancellationToken cancellationToken)
    {
        var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var profile = await profiles.GetCurrentAsync(cancellationToken);
        if (profile is not null)
        {
            answers["Full name"] = profile.Name;
            var parts = profile.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) { answers["First name"] = parts[0]; answers["Last name"] = parts[1]; }
        }
        var prefs = await preferences.GetAsync(cancellationToken);
        if (prefs.EffectiveLinkedInProfileUrl is { } linkedIn) answers["LinkedIn"] = linkedIn;
        if (!string.IsNullOrWhiteSpace(prefs.SalaryExpectation)) answers["Salary expectation"] = prefs.SalaryExpectation;
        if (!string.IsNullOrWhiteSpace(prefs.WorkAuthorization)) answers["Work authorization"] = prefs.WorkAuthorization;
        foreach (var fact in await facts.ListVerifiedAsync(cancellationToken))
            answers[fact.Name] = fact.Value;
        return answers;
    }
}
