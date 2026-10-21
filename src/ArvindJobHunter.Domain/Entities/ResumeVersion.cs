namespace ArvindJobHunter.Domain.Entities;

public sealed record ResumeVersion(
    Guid Id,
    Guid JobId,
    string MasterPath,
    string? OutputPath,
    ResumeVersionStatus Status,
    IReadOnlyList<ResumeChange> Changes,
    Guid? ApprovalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PdfPath = null,
    string? PdfSha256 = null,
    string? CountryCode = null,
    ResumeTemplateChoice Template = ResumeTemplateChoice.KeepMasterTemplate)
{
    public static ResumeVersion Propose(Guid jobId, string masterPath, IReadOnlyList<ResumeChange> changes, string? countryCode = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ResumeVersion(Guid.NewGuid(), jobId, masterPath, null, ResumeVersionStatus.PROPOSED, changes, null, now, now, CountryCode: countryCode);
    }

    // The template line is only added for non-default choices so approvals made before templates existed keep their hash.
    public string PayloadForApproval() =>
        string.Join("\n", Changes.Select(c => $"{c.Section}|{c.OldText}|{c.NewText}")) +
        (Template == ResumeTemplateChoice.KeepMasterTemplate ? "" : $"\nTEMPLATE|{Template}|{CountryCode}|{MasterPath}");

    public ResumeVersion WithTemplate(ResumeTemplateChoice template, string masterPath) =>
        this with { Template = template, MasterPath = masterPath, Status = ResumeVersionStatus.PROPOSED, ApprovalId = null, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion WithApproval(Guid approvalId) =>
        this with { ApprovalId = approvalId, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkApproved() => this with { Status = ResumeVersionStatus.APPROVED, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkRejected() => this with { Status = ResumeVersionStatus.REJECTED, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkGenerated(string outputPath) =>
        this with { Status = ResumeVersionStatus.GENERATED, OutputPath = outputPath, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion WithPdf(string pdfPath, string sha256) =>
        this with { PdfPath = pdfPath, PdfSha256 = sha256, UpdatedAt = DateTimeOffset.UtcNow };
}

/// <summary>Keep the user's own master layout, or use the master uploaded for the job's country.</summary>
public enum ResumeTemplateChoice { KeepMasterTemplate, CountryTemplate }

public sealed record ResumeChange(
    string Section,
    string OldText,
    string NewText,
    IReadOnlyList<string> Evidence);
