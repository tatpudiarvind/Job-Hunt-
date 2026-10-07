using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class ResumeService(IRepository<ResumeVersion> repository, ApprovalService approvals, IRuntimeSettingsProvider settings, AuditService audit)
{
    public async Task<IReadOnlyList<ResumeVersion>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(r => r.CreatedAt).ToList();

    public Task<ResumeVersion?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<ResumeVersion> ProposeAsync(Guid jobId, IReadOnlyList<ResumeChange> changes, Guid userId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var version = ResumeVersion.Propose(jobId, current.MasterResumePath, changes);
        await repository.UpsertAsync(version, cancellationToken);
        await audit.RecordAsync(userId, "RESUME_PROPOSED", "ResumeVersion", version.Id.ToString(), "PROPOSED", $"{changes.Count} change(s)", cancellationToken);
        return version;
    }

    public async Task<ApprovalRequest?> RequestApprovalAsync(Guid versionId, Guid userId, CancellationToken cancellationToken)
    {
        var version = await repository.GetAsync(versionId, cancellationToken);
        if (version is null) return null;
        var approval = await approvals.RequestAsync(
            ApprovalActionType.APPLY_RESUME_CHANGES, nameof(ResumeVersion), version.Id, version.PayloadForApproval(),
            $"Apply {version.Changes.Count} resume change(s) for job {version.JobId}", userId, cancellationToken);
        await repository.UpsertAsync(version.WithApproval(approval.Id), cancellationToken);
        return approval;
    }

    public Task SaveAsync(ResumeVersion version, CancellationToken cancellationToken) => repository.UpsertAsync(version, cancellationToken);
}
