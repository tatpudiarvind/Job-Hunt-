using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class JobService(IRepository<Job> jobs, IRepository<JobApplication> applications, AuditService audit)
{
    public async Task<IReadOnlyList<Job>> ListAsync(CancellationToken cancellationToken) =>
        (await jobs.ListAsync(cancellationToken)).OrderByDescending(j => j.CreatedAt).ToList();

    public Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken) => jobs.GetAsync(id, cancellationToken);

    public async Task<Job> CreateAsync(string title, string company, string? location, string? source, string? url, string description, Guid userId, CancellationToken cancellationToken)
    {
        var job = Job.Create(title, company, location ?? "", source ?? "MANUAL", url, description);
        await jobs.UpsertAsync(job, cancellationToken);
        await applications.UpsertAsync(JobApplication.Create(job.Id), cancellationToken);
        await audit.RecordAsync(userId, "JOB_CREATED", "Job", job.Id.ToString(), "SUCCESS", $"{job.Title} @ {job.Company}", cancellationToken);
        return job;
    }

    public Task SaveAsync(Job job, CancellationToken cancellationToken) => jobs.UpsertAsync(job, cancellationToken);

    public async Task<Job?> DismissAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(id, cancellationToken);
        if (job is null) return null;
        var dismissed = job.Dismiss();
        await jobs.UpsertAsync(dismissed, cancellationToken);
        await audit.RecordAsync(userId, "JOB_DISMISSED", "Job", id.ToString(), "SUCCESS", null, cancellationToken);
        return dismissed;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var deleted = await jobs.DeleteAsync(id, cancellationToken);
        if (!deleted) return false;
        foreach (var app in (await applications.ListAsync(cancellationToken)).Where(a => a.JobId == id))
        {
            await applications.DeleteAsync(app.Id, cancellationToken);
        }

        await audit.RecordAsync(userId, "JOB_DELETED", "Job", id.ToString(), "SUCCESS", null, cancellationToken);
        return true;
    }
}
