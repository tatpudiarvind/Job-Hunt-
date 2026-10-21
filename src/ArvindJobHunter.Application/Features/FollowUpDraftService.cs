using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

/// <summary>
/// Drafts a polite follow-up email for an application. It only creates a local draft;
/// sending still requires the normal email approval and execution.
/// </summary>
public sealed class FollowUpDraftService(
    IRepository<JobApplication> applications,
    IRepository<Job> jobs,
    CandidateProfileService profiles,
    EmailDraftService emails)
{
    public async Task<EmailDraft?> DraftAsync(Guid applicationId, Guid userId, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(applicationId, cancellationToken);
        if (application is null) return null;
        var job = await jobs.GetAsync(application.JobId, cancellationToken) ?? throw new KeyNotFoundException("Job not found.");
        var profile = await profiles.GetCurrentAsync(cancellationToken);
        var (subject, body) = Compose(job, application, profile?.Name);
        return await emails.CreateAsync(job.Id, "FOLLOW_UP", job.Recruiter?.Email ?? "", subject, body, userId, cancellationToken);
    }

    public static (string Subject, string Body) Compose(Job job, JobApplication application, string? candidateName)
    {
        var greeting = string.IsNullOrWhiteSpace(job.Recruiter?.Name) ? "Hello" : $"Hello {job.Recruiter!.Name}";
        var appliedOn = application.History.LastOrDefault(h => h.To == ApplicationStatus.APPLIED)?.At ?? application.UpdatedAt;
        var name = string.IsNullOrWhiteSpace(candidateName) ? "" : $"\n{candidateName}";
        var subject = $"Following up: {job.Title} application";
        var body =
            $"{greeting},\n\n" +
            $"I applied for the {job.Title} role at {job.Company} on {appliedOn:d MMMM yyyy} and wanted to follow up. " +
            "I remain very interested in the position and would be glad to share any further information that would help.\n\n" +
            $"Thank you for your time.\n\nKind regards,{name}";
        return (subject, body);
    }
}
