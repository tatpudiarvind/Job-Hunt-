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
    ILogger<ResumeService> logger,
    ITemplateFidelityChecker? fidelity = null)
{
    public async Task<IReadOnlyList<ResumeVersion>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(r => r.CreatedAt).ToList();

    public Task<ResumeVersion?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<ResumeVersion> ProposeAsync(Guid jobId, IReadOnlyList<ResumeChange> changes, Guid userId, CancellationToken cancellationToken, string? countryCode = null)
    {
        var current = await settings.GetAsync(cancellationToken);
        var code = CountryProfiles.Normalize(countryCode);
        if (code is not null)
        {
            var spelling = CountryProfiles.Get(code).Spelling;
            changes = changes.Select(c => CountryResumeAdvisor.ToSpelling(c, spelling)).ToList();
        }
        var template = code is not null && current.CountryResumePaths.ContainsKey(code) ? ResumeTemplateChoice.CountryTemplate : ResumeTemplateChoice.KeepMasterTemplate;
        var version = ResumeVersion.Propose(jobId, current.MasterResumeFor(code), changes, code) with { Template = template };
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
        var summary = $"Apply {version.Changes.Count} resume change(s) for job {version.JobId}";
        if (MasterResumeLayout.WarningFor(version.MasterPath) is { } layoutWarning) summary += $"\n\nWarning: {layoutWarning}";
        else if (await CheckFidelityAsync(version, cancellationToken) is { TemplatePreserved: false } report)
            summary += "\n\nTemplate check:\n- " + string.Join("\n- ", report.Issues);
        if (version.CountryCode is not null && await CountryIssuesAsync(version, cancellationToken) is { Count: > 0 } countryIssues)
            summary += $"\n\n{CountryProfiles.Get(version.CountryCode).Name} conventions:\n- " + string.Join("\n- ", countryIssues);
        var approval = await approvals.RequestAsync(
            ApprovalActionType.APPLY_RESUME_CHANGES, nameof(ResumeVersion), version.Id, version.PayloadForApproval(),
            summary, userId, cancellationToken);
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
            var report = TryCompare(version.MasterPath, previewPath);
            return new ResumePreviewResponse(
                version.Id,
                version.JobId,
                version.Status.ToString(),
                previewDocument.Paragraphs.Select(p => new ResumePreviewParagraph(p.Index, p.Text, p.Style)).ToList(),
                DateTimeOffset.UtcNow,
                report is null ? null : new TemplateFidelityResponse(report.TemplatePreserved, report.Issues, report.MasterPageEstimate, report.TailoredPageEstimate));
        }
        finally
        {
            if (generatedPreview)
            {
                try { File.Delete(previewPath); } catch { }
            }
        }
    }

    private TemplateFidelityReport? TryCompare(string masterPath, string tailoredPath)
    {
        if (fidelity is null) return null;
        try { return fidelity.Compare(masterPath, tailoredPath); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Template check failed for {Tailored}", tailoredPath);
            return null;
        }
    }

    /// <summary>Builds a temporary tailored copy (never the master) to compare it with the template.</summary>
    private async Task<TemplateFidelityReport?> CheckFidelityAsync(ResumeVersion version, CancellationToken cancellationToken)
    {
        if (fidelity is null || string.IsNullOrWhiteSpace(version.MasterPath) || !File.Exists(version.MasterPath)) return null;
        var directory = Path.Combine(Path.GetTempPath(), "JobHunter", "resume-previews");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{version.Id:N}-check-{Guid.NewGuid():N}.docx");
        try
        {
            await documents.ApplyChangesAsync(version.MasterPath, path, version.Changes, cancellationToken);
            return TryCompare(version.MasterPath, path);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "Template check skipped for resume version {VersionId}", version.Id);
            return null;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>Country rule check on the master text with the proposed changes applied in memory.</summary>
    public async Task<IReadOnlyList<string>> CountryIssuesAsync(ResumeVersion version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(version.MasterPath) || !File.Exists(version.MasterPath)) return [];
        try
        {
            var document = await documents.ReadAsync(version.MasterPath, cancellationToken);
            var paragraphs = document.Paragraphs.Select(p =>
                version.Changes.Aggregate(p.Text, (text, c) => c.OldText.Length > 0 && text.Contains(c.OldText, StringComparison.Ordinal) ? text.Replace(c.OldText, c.NewText, StringComparison.Ordinal) : text)).ToList();
            return CountryResumeAdvisor.Check(paragraphs, CountryProfiles.Get(version.CountryCode));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "Country check skipped for resume version {VersionId}", version.Id);
            return [];
        }
    }

    /// <summary>Switches the template; any earlier approval no longer matches and is invalidated.</summary>
    public async Task<ResumeVersion?> SetTemplateAsync(Guid id, ResumeTemplateChoice template, Guid userId, CancellationToken cancellationToken)
    {
        var version = await repository.GetAsync(id, cancellationToken);
        if (version is null) return null;
        if (version.Status == ResumeVersionStatus.GENERATED)
            throw new InvalidOperationException("This resume was already generated; prepare the application again to change its template.");
        var current = await settings.GetAsync(cancellationToken);
        string path;
        if (template == ResumeTemplateChoice.CountryTemplate)
        {
            if (version.CountryCode is null || !current.CountryResumePaths.ContainsKey(version.CountryCode))
                throw new InvalidOperationException($"No resume template is uploaded for {(version.CountryCode is null ? "this job's country" : CountryProfiles.Get(version.CountryCode).Name)}. Upload one in Settings.");
            path = current.MasterResumeFor(version.CountryCode);
        }
        else path = current.MasterResumePath;

        var updated = version.WithTemplate(template, path);
        await repository.UpsertAsync(updated, cancellationToken);
        await approvals.InvalidateForTargetAsync(version.Id, $"Template changed to {template}.", userId, cancellationToken);
        await audit.RecordAsync(userId, "RESUME_TEMPLATE_CHANGED", "ResumeVersion", id.ToString(), "SUCCESS", $"{template}: {Path.GetFileName(path)}", cancellationToken);
        return updated;
    }

    public async Task SaveAsync(ResumeVersion version, CancellationToken cancellationToken)
    {
        await repository.UpsertAsync(version, cancellationToken);
        logger.LogInformation("Resume version {VersionId} saved for job {JobId}", version.Id, version.JobId);
    }
}
