namespace ArvindJobHunter.Domain.Entities;

public sealed record JobApplication(
    Guid Id,
    Guid JobId,
    ApplicationStatus Status,
    Guid? ResumeVersionId,
    Guid? CoverLetterDraftId,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ApplicationTransition> History,
    DateTimeOffset? FollowUpDueAt = null,
    string? FollowUpNote = null)
{
    private static readonly Dictionary<ApplicationStatus, ApplicationStatus[]> Allowed = new()
    {
        [ApplicationStatus.DISCOVERED] = [ApplicationStatus.MATCHED, ApplicationStatus.CLOSED],
        [ApplicationStatus.MATCHED] = [ApplicationStatus.SHORTLISTED, ApplicationStatus.CLOSED],
        [ApplicationStatus.SHORTLISTED] = [ApplicationStatus.RESUME_PREPARED, ApplicationStatus.WITHDRAWN, ApplicationStatus.CLOSED],
        [ApplicationStatus.RESUME_PREPARED] = [ApplicationStatus.AWAITING_APPROVAL, ApplicationStatus.SHORTLISTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.AWAITING_APPROVAL] = [ApplicationStatus.APPROVED, ApplicationStatus.RESUME_PREPARED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.APPROVED] = [ApplicationStatus.APPLIED, ApplicationStatus.AWAITING_APPROVAL, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.APPLIED] = [ApplicationStatus.RECRUITER_CONTACTED, ApplicationStatus.RECRUITER_REPLIED, ApplicationStatus.INTERVIEW, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.RECRUITER_CONTACTED] = [ApplicationStatus.RECRUITER_REPLIED, ApplicationStatus.INTERVIEW, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.RECRUITER_REPLIED] = [ApplicationStatus.INTERVIEW, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.INTERVIEW] = [ApplicationStatus.TECHNICAL_INTERVIEW, ApplicationStatus.HR_INTERVIEW, ApplicationStatus.OFFER, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.TECHNICAL_INTERVIEW] = [ApplicationStatus.HR_INTERVIEW, ApplicationStatus.OFFER, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.HR_INTERVIEW] = [ApplicationStatus.OFFER, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.OFFER] = [ApplicationStatus.CLOSED, ApplicationStatus.REJECTED, ApplicationStatus.WITHDRAWN],
        [ApplicationStatus.REJECTED] = [ApplicationStatus.CLOSED],
        [ApplicationStatus.WITHDRAWN] = [ApplicationStatus.CLOSED],
        [ApplicationStatus.CLOSED] = []
    };

    public static JobApplication Create(Guid jobId, ApplicationStatus initial = ApplicationStatus.DISCOVERED)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobApplication(Guid.NewGuid(), jobId, initial, null, null, null, now, now,
            [new ApplicationTransition(null, initial, now, "Created")]);
    }

    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public JobApplication TransitionTo(ApplicationStatus to, string reason)
    {
        if (!CanTransition(Status, to))
        {
            throw new InvalidOperationException($"Application cannot move from {Status} to {to}.");
        }

        var now = DateTimeOffset.UtcNow;
        return this with
        {
            Status = to,
            UpdatedAt = now,
            History = [.. History, new ApplicationTransition(Status, to, now, reason)]
        };
    }

    public JobApplication WithResume(Guid resumeVersionId) =>
        this with { ResumeVersionId = resumeVersionId, UpdatedAt = DateTimeOffset.UtcNow };

    public JobApplication WithCoverLetter(Guid draftId) =>
        this with { CoverLetterDraftId = draftId, UpdatedAt = DateTimeOffset.UtcNow };

    /// <summary>Statuses where a polite follow-up is appropriate and a reminder is suggested by default.</summary>
    public static readonly IReadOnlySet<ApplicationStatus> FollowUpEligible = new HashSet<ApplicationStatus>
    {
        ApplicationStatus.APPLIED, ApplicationStatus.RECRUITER_CONTACTED, ApplicationStatus.RECRUITER_REPLIED,
        ApplicationStatus.INTERVIEW, ApplicationStatus.TECHNICAL_INTERVIEW, ApplicationStatus.HR_INTERVIEW
    };

    public bool IsFollowUpDue(DateTimeOffset now) => FollowUpDueAt is { } due && due <= now && FollowUpEligible.Contains(Status);

    public JobApplication ScheduleFollowUp(DateTimeOffset dueAt, string? note) =>
        this with { FollowUpDueAt = dueAt, FollowUpNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), UpdatedAt = DateTimeOffset.UtcNow };

    public JobApplication ClearFollowUp() =>
        this with { FollowUpDueAt = null, FollowUpNote = null, UpdatedAt = DateTimeOffset.UtcNow };
}

public sealed record ApplicationTransition(ApplicationStatus? From, ApplicationStatus To, DateTimeOffset At, string Reason);
