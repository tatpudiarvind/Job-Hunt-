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
    DateTimeOffset UpdatedAt)
{
    public static ResumeVersion Propose(Guid jobId, string masterPath, IReadOnlyList<ResumeChange> changes)
    {
        var now = DateTimeOffset.UtcNow;
        return new ResumeVersion(Guid.NewGuid(), jobId, masterPath, null, ResumeVersionStatus.PROPOSED, changes, null, now, now);
    }

    public string PayloadForApproval() =>
        string.Join("\n", Changes.Select(c => $"{c.Section}|{c.OldText}|{c.NewText}"));

    public ResumeVersion WithApproval(Guid approvalId) =>
        this with { ApprovalId = approvalId, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkApproved() => this with { Status = ResumeVersionStatus.APPROVED, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkRejected() => this with { Status = ResumeVersionStatus.REJECTED, UpdatedAt = DateTimeOffset.UtcNow };

    public ResumeVersion MarkGenerated(string outputPath) =>
        this with { Status = ResumeVersionStatus.GENERATED, OutputPath = outputPath, UpdatedAt = DateTimeOffset.UtcNow };
}

public sealed record ResumeChange(
    string Section,
    string OldText,
    string NewText,
    IReadOnlyList<string> Evidence);
