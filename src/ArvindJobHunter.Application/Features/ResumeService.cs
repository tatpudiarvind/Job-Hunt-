using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class ResumeService(
    IRepository<ResumeVersion> repository,
    ApprovalService approvals,
    IRuntimeSettingsProvider settings,
    IResumeDocumentService documents,
    AuditService audit,
    ILogger<ResumeService> logger)
{
    public async Task<IReadOnlyList<ResumeVersion>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(r => r.CreatedAt).ToList();

    public Task<ResumeVersion?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<ResumeVersion> ProposeAsync(Guid jobId, IReadOnlyList<ResumeChange> changes, Guid userId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var version = ResumeVersion.Propose(jobId, current.MasterResumePath, changes);
        await repository.UpsertAsync(version, cancellationToken);
        logger.LogInformation("Resume version {VersionId} proposed for job {JobId} with {ChangeCount} change(s)", version.Id, jobId, changes.Count);
        await audit.RecordAsync(userId, "RESUME_PROPOSED", "ResumeVersion", version.Id.ToString(), "PROPOSED", $"{changes.Count} change(s)", cancellationToken);
        return version;
    }

    public async Task<ApprovalRequest?> RequestApprovalAsync(Guid versionId, Guid userId, CancellationToken cancellationToken)
    {
        var version = await repository.GetAsync(versionId, cancellationToken);
        if (version is null)
        {
            logger.LogWarning("Resume approval requested for version {VersionId}, but the version was not found", versionId);
            return null;
        }
        var approval = await approvals.RequestAsync(
            ApprovalActionType.APPLY_RESUME_CHANGES, nameof(ResumeVersion), version.Id, version.PayloadForApproval(),
            $"Apply {version.Changes.Count} resume change(s) for job {version.JobId}", userId, cancellationToken);
        await repository.UpsertAsync(version.WithApproval(approval.Id), cancellationToken);
        logger.LogInformation("Approval {ApprovalId} requested for resume version {VersionId}", approval.Id, versionId);
        return approval;
    }

    public async Task<ResumePreviewResponse?> PreviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var version = await repository.GetAsync(id, cancellationToken);
        if (version is null) return null;

        var previewPath = version.OutputPath;
        var generatedPreview = false;
        if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
        {
            if (string.IsNullOrWhiteSpace(version.MasterPath) || !File.Exists(version.MasterPath))
            {
                throw new FileNotFoundException("Master resume not found.", version.MasterPath);
            }

            var previewDirectory = Path.Combine(Path.GetTempPath(), "JobHunter", "resume-previews");
            Directory.CreateDirectory(previewDirectory);
            previewPath = Path.Combine(previewDirectory, $"{version.Id:N}-{Guid.NewGuid():N}.docx");
            await documents.ApplyChangesAsync(version.MasterPath, previewPath, version.Changes, cancellationToken);
            generatedPreview = true;
        }

        try
        {
            var previewDocument = await documents.ReadAsync(previewPath, cancellationToken);
            return new ResumePreviewResponse(
                version.Id,
                version.JobId,
                version.Status.ToString(),
                previewDocument.Paragraphs.Select(p => new ResumePreviewParagraph(p.Index, p.Text, p.Style)).ToList(),
                DateTimeOffset.UtcNow);
        }
        finally
        {
            if (generatedPreview)
            {
                try { File.Delete(previewPath); } catch { }
            }
        }
    }

    public async Task SaveAsync(ResumeVersion version, CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(version, cancellationToken);
        logger.LogInformation("Resume version {VersionId} saved for job {JobId}", version.Id, version.JobId);
    }
}
