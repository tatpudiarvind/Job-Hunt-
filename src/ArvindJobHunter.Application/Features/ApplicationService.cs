using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class ApplicationService(IRepository<JobApplication> repository, AuditService audit, ILogger<ApplicationService> logger)
{
    public async Task<IReadOnlyList<JobApplication>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(a => a.UpdatedAt).ToList();

    public Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<JobApplication?> GetByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).FirstOrDefault(a => a.JobId == jobId);

    public async Task<JobApplication> EnsureForJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var existing = await GetByJobAsync(jobId, cancellationToken);
        if (existing is not null)
        {
            logger.LogDebug("Application {ApplicationId} already exists for job {JobId}", existing.Id, jobId);
            return existing;
        }
        var created = JobApplication.Create(jobId);
        await repository.UpsertAsync(created, cancellationToken);
        logger.LogInformation("Application {ApplicationId} created for job {JobId}", created.Id, jobId);
        return created;
    }

    public async Task<JobApplication?> TransitionAsync(Guid id, ApplicationStatus to, string reason, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null)
        {
            logger.LogWarning("Application {ApplicationId} could not transition to {TargetStatus} because it was not found", id, to);
            return null;
        }
        var updated = application.TransitionTo(to, reason);
        await repository.UpsertAsync(updated, cancellationToken);
        logger.LogInformation("Application {ApplicationId} transitioned from {FromStatus} to {ToStatus}", id, application.Status, to);
        await audit.RecordAsync(userId, "APPLICATION_TRANSITION", "JobApplication", id.ToString(), to.ToString(), $"{application.Status} -> {to}: {reason}", cancellationToken);
        return updated;
    }

    /// <summary>Moves the application forward only if the transition is valid; otherwise leaves it unchanged.</summary>
    public async Task<JobApplication> TryAdvanceAsync(JobApplication application, ApplicationStatus to, string reason, Guid userId, CancellationToken cancellationToken)
    {
        if (application.Status == to)
        {
            logger.LogDebug("Application {ApplicationId} is already in status {Status}; no advance needed", application.Id, to);
            return application;
        }

        if (!JobApplication.CanTransition(application.Status, to))
        {
            logger.LogDebug("Application {ApplicationId} cannot transition from {FromStatus} to {ToStatus}; request ignored", application.Id, application.Status, to);
            return application;
        }

        var updated = application.TransitionTo(to, reason);
        await repository.UpsertAsync(updated, cancellationToken);
        logger.LogInformation("Application {ApplicationId} advanced from {FromStatus} to {ToStatus}", application.Id, application.Status, to);
        await audit.RecordAsync(userId, "APPLICATION_TRANSITION", "JobApplication", application.Id.ToString(), to.ToString(), reason, cancellationToken);
        return updated;
    }

    public async Task SaveAsync(JobApplication application, CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(application, cancellationToken);
        logger.LogInformation("Application {ApplicationId} saved with status {Status}", application.Id, application.Status);
    }

    public async Task<JobApplication?> ScheduleFollowUpAsync(Guid id, DateTimeOffset dueAt, string? note, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null)
        {
            logger.LogWarning("Follow-up for application {ApplicationId} could not be scheduled because it was not found", id);
            return null;
        }
        var updated = application.ScheduleFollowUp(dueAt, note);
        await repository.UpsertAsync(updated, cancellationToken);
        logger.LogInformation("Follow-up scheduled for application {ApplicationId} at {DueAt:u}", id, dueAt);
        await audit.RecordAsync(userId, "FOLLOW_UP_SCHEDULED", "JobApplication", id.ToString(), "OK", $"Due {dueAt:u}", cancellationToken);
        return updated;
    }

    public async Task<JobApplication?> ClearFollowUpAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var application = await repository.GetAsync(id, cancellationToken);
        if (application is null)
        {
            logger.LogWarning("Follow-up for application {ApplicationId} could not be cleared because it was not found", id);
            return null;
        }
        var updated = application.ClearFollowUp();
        await repository.UpsertAsync(updated, cancellationToken);
        logger.LogInformation("Follow-up cleared for application {ApplicationId}", id);
        await audit.RecordAsync(userId, "FOLLOW_UP_CLEARED", "JobApplication", id.ToString(), "OK", null, cancellationToken);
        return updated;
    }

    /// <summary>Applications whose follow-up reminder is due. Reminders only; no email is sent without a separate approval.</summary>
    public async Task<IReadOnlyList<JobApplication>> DueFollowUpsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = (await repository.ListAsync(cancellationToken)).Where(a => a.IsFollowUpDue(now)).OrderBy(a => a.FollowUpDueAt).ToList();
        logger.LogDebug("Found {DueCount} application follow-up(s) due at {CheckedAt:u}", due.Count, now);
        return due;
    }
}
