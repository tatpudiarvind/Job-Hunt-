using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class ApplicationService(IRepository<JobApplication> repository, AuditService audit)
{
    public async Task<IReadOnlyList<JobApplication>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(a => a.UpdatedAt).ToList();

    public Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<JobApplication?> GetByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).FirstOrDefault(a => a.JobId == jobId);

    public async Task<JobApplication> EnsureForJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var existing = await GetByJobAsync(jobId, cancellationToken);
        if (existing is not null) return existing;
        var created = JobApplication.Create(jobId);
        await repository.UpsertAsync(created, cancellationToken);
        return created;
    }

    public async Task<JobApplication?> TransitionAsync(Guid id, ApplicationStatus to, string reason, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null) return null;
        var updated = application.TransitionTo(to, reason);
        await repository.UpsertAsync(updated, cancellationToken);
        await audit.RecordAsync(userId, "APPLICATION_TRANSITION", "JobApplication", id.ToString(), to.ToString(), $"{application.Status} -> {to}: {reason}", cancellationToken);
        return updated;
    }

    /// <summary>Moves the application forward only if the transition is valid; otherwise leaves it unchanged.</summary>
    public async Task<JobApplication> TryAdvanceAsync(JobApplication application, ApplicationStatus to, string reason, Guid userId, CancellationToken cancellationToken)
    {
        if (application.Status == to || !JobApplication.CanTransition(application.Status, to)) return application;
        var updated = application.TransitionTo(to, reason);
        await repository.UpsertAsync(updated, cancellationToken);
        await audit.RecordAsync(userId, "APPLICATION_TRANSITION", "JobApplication", application.Id.ToString(), to.ToString(), reason, cancellationToken);
        return updated;
    }

    public Task SaveAsync(JobApplication application, CancellationToken cancellationToken) => repository.UpsertAsync(application, cancellationToken);

    public async Task<JobApplication?> ScheduleFollowUpAsync(Guid id, DateTimeOffset dueAt, string? note, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null) return null;
        var updated = application.ScheduleFollowUp(dueAt, note);
        await repository.UpsertAsync(updated, cancellationToken);
        await audit.RecordAsync(userId, "FOLLOW_UP_SCHEDULED", "JobApplication", id.ToString(), "OK", $"Due {dueAt:u}", cancellationToken);
        return updated;
    }

    public async Task<JobApplication?> ClearFollowUpAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null) return null;
        var updated = application.ClearFollowUp();
        await repository.UpsertAsync(updated, cancellationToken);
        await audit.RecordAsync(userId, "FOLLOW_UP_CLEARED", "JobApplication", id.ToString(), "OK", null, cancellationToken);
        return updated;
    }

    /// <summary>Applications whose follow-up reminder is due. Reminders only; no email is sent without a separate approval.</summary>
    public async Task<IReadOnlyList<JobApplication>> DueFollowUpsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return (await repository.ListAsync(cancellationToken)).Where(a => a.IsFollowUpDue(now)).OrderBy(a => a.FollowUpDueAt).ToList();
    }
}
