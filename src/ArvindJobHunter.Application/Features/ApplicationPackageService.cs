using System.Security.Cryptography;
using System.Text;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed record ApplicationReview(
    JobApplication Application,
    Job Job,
    ResumeVersion? Resume,
    EmailDraft? CoverLetter,
    ApplicationPackage? LatestPackage,
    bool PackageIsCurrent,
    IReadOnlyList<string> Issues);

/// <summary>
/// Builds immutable, hash-bound application packages and checks them for consistency before anything is submitted.
/// </summary>
public sealed class ApplicationPackageService(
    IRepository<ApplicationPackage> packages,
    IRepository<JobApplication> applications,
    IRepository<Job> jobs,
    IRepository<ResumeVersion> resumes,
    IRepository<EmailDraft> emails,
    AuditService audit,
    ILogger<ApplicationPackageService> logger)
{
    public async Task<ApplicationReview?> ReviewAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken);
        if (application is null) return null;
        var job = await jobs.GetAsync(application.JobId, cancellationToken);
        if (job is null) return null;
        if (job.Sponsorship is null || job.Sponsorship.IsStale(DateTimeOffset.UtcNow))
        {
            job = job with { Sponsorship = JobDiscoveryService.EvaluateSponsorship(job.Title, job.Description, job.Location, job.Source) };
            await jobs.UpsertAsync(job, cancellationToken);
        }

        var resume = application.ResumeVersionId is { } resumeId ? await resumes.GetAsync(resumeId, cancellationToken) : null;
        var cover = application.CoverLetterDraftId is { } coverId ? await emails.GetAsync(coverId, cancellationToken) : null;
        var latest = (await packages.ListAsync(cancellationToken))
            .Where(p => p.ApplicationId == applicationId)
            .MaxBy(p => p.Version);

        var issues = CheckConsistency(application, job, resume, cover);
        var current = latest is not null && latest.PackageHash == PackageHash(job, resume, cover);
        return new ApplicationReview(application, job, resume, cover, latest, current, issues);
    }

    public async Task<IReadOnlyList<ApplicationPackage>> HistoryAsync(Guid applicationId, CancellationToken cancellationToken) =>
        (await packages.ListAsync(cancellationToken)).Where(p => p.ApplicationId == applicationId).OrderByDescending(p => p.Version).ToList();

    /// <summary>Freezes the current job, resume and cover letter. Refuses when consistency checks fail.</summary>
    public async Task<ApplicationPackage?> CreatePackageAsync(Guid applicationId, Guid userId, CancellationToken cancellationToken)
    {
        var review = await ReviewAsync(applicationId, cancellationToken);
        if (review is null) return null;
        if (review.Issues.Count > 0)
            throw new InvalidOperationException("Package cannot be created: " + string.Join(" ", review.Issues));
        if (review.PackageIsCurrent && review.LatestPackage is not null) return review.LatestPackage;

        var package = new ApplicationPackage(
            Guid.NewGuid(),
            review.Application.Id,
            review.Job.Id,
            (review.LatestPackage?.Version ?? 0) + 1,
            Hash(JobSnapshot(review.Job)),
            review.Resume?.Id,
            review.Resume is null ? null : Hash(review.Resume.PayloadForApproval()),
            review.CoverLetter?.Id,
            review.CoverLetter is null ? null : Hash(review.CoverLetter.PayloadForApproval()),
            PackageHash(review.Job, review.Resume, review.CoverLetter),
            userId,
            DateTimeOffset.UtcNow);

        await packages.UpsertAsync(package, cancellationToken);
        await audit.RecordAsync(userId, "APPLICATION_PACKAGE_CREATED", "Application", applicationId.ToString(), "SUCCESS",
            $"Version={package.Version}; Hash={package.PackageHash[..12]}", cancellationToken);
        logger.LogInformation("Application package v{Version} created for application {ApplicationId}", package.Version, applicationId);
        return package;
    }

    public static IReadOnlyList<string> CheckConsistency(JobApplication application, Job job, ResumeVersion? resume, EmailDraft? cover)
    {
        var issues = new List<string>();
        if (resume is null) issues.Add("No tailored resume is attached.");
        else
        {
            if (resume.JobId != job.Id) issues.Add("The attached resume was prepared for a different job.");
            if (resume.Status != ResumeVersionStatus.GENERATED) issues.Add("The attached resume has not been approved and generated yet.");
        }

        if (cover is not null)
        {
            if (cover.JobId is { } coverJob && coverJob != job.Id) issues.Add("The attached cover letter belongs to a different job.");
            if (!MentionsCompany(cover, job.Company))
                issues.Add($"The cover letter does not mention {job.Company}.");
        }

        if (job.Sponsorship is { Status: SponsorshipStatus.EXCLUDED })
            issues.Add("The posting states that visa sponsorship is not available.");
        if (application.Status is ApplicationStatus.CLOSED or ApplicationStatus.WITHDRAWN)
            issues.Add($"The application is {application.Status}.");
        return issues;
    }

    private static readonly string[] LegalSuffixes =
        ["gmbh", "ag", "ltd", "limited", "inc", "llc", "plc", "bv", "b.v.", "nv", "sa", "sas", "sarl", "srl", "spa", "ab", "as", "oy", "se", "co", "corp", "corporation", "group", "&"];

    /// <summary>Accepts "Contoso" for "Contoso GmbH": legal-form suffixes and punctuation are ignored.</summary>
    public static bool MentionsCompany(EmailDraft cover, string company)
    {
        if (string.IsNullOrWhiteSpace(company)) return true;
        var text = $"{cover.Subject}\n{cover.Body}";
        if (text.Contains(company, StringComparison.OrdinalIgnoreCase)) return true;
        var core = string.Join(' ', company
            .Split([' ', ',', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !LegalSuffixes.Contains(w.ToLowerInvariant())));
        return core.Length >= 2 && text.Contains(core, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a package was frozen for this content set and the content has since changed.</summary>
    public static bool IsStale(ApplicationPackage? latest, Job job, ResumeVersion? resume, EmailDraft? cover) =>
        latest is not null && latest.PackageHash != PackageHash(job, resume, cover);

    public static string JobSnapshot(Job job) => $"{job.Title}|{job.Company}|{job.Location}|{job.Url}|{job.Description}";

    public static string PackageHash(Job job, ResumeVersion? resume, EmailDraft? cover) =>
        Hash($"{JobSnapshot(job)}\n--resume:{resume?.Id}:{resume?.PayloadForApproval()}\n--cover:{cover?.Id}:{cover?.PayloadForApproval()}");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
