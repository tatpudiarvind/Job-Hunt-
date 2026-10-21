using System.Text;

namespace ArvindJobHunter.Domain.Entities;

public enum SubmissionStatus
{
    PREPARED,
    NEEDS_ANSWERS,
    AWAITING_APPROVAL,
    SUBMITTED,
    FAILED
}

/// <summary>One form field the browser filled (or could not fill) on an application page.</summary>
public sealed record SubmissionField(string Label, string? Value, bool Required, string Source);

/// <summary>
/// What the browser filled in on a job site, frozen for review. The user approves exactly this payload;
/// any change (answers, resume file, cover letter) produces a different hash and invalidates the approval.
/// </summary>
public sealed record SubmissionPackage(
    Guid Id,
    Guid JobId,
    Guid ApplicationId,
    string Url,
    string Adapter,
    string ResumeFilePath,
    string ResumeSha256,
    string? CoverLetterText,
    IReadOnlyList<SubmissionField> Fields,
    IReadOnlyList<string> UnansweredFields,
    string? ScreenshotPath,
    SubmissionStatus Status,
    Guid? ApprovalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? SessionId = null,
    string? ConfirmationText = null)
{
    public bool ReadyForApproval => UnansweredFields.Count == 0;

    public string PayloadForApproval()
    {
        var builder = new StringBuilder()
            .Append("SUBMIT|").Append(JobId).Append('|').Append(Url).Append('|').Append(Adapter).Append('\n')
            .Append("resume:").Append(ResumeSha256).Append('\n')
            .Append("cover:").Append(CoverLetterText).Append('\n');
        foreach (var field in Fields.OrderBy(f => f.Label, StringComparer.Ordinal))
            builder.Append(field.Label).Append('=').Append(field.Value).Append('\n');
        return builder.ToString();
    }

    /// <summary>Applies the user's answers; clears the approval because the payload changed.</summary>
    public SubmissionPackage Answer(IReadOnlyDictionary<string, string> answers)
    {
        var fields = Fields.Select(f => answers.TryGetValue(f.Label, out var value) ? f with { Value = value.Trim(), Source = "user" } : f).ToList();
        foreach (var (label, value) in answers)
            if (fields.All(f => !string.Equals(f.Label, label, StringComparison.Ordinal))) fields.Add(new SubmissionField(label, value.Trim(), true, "user"));
        var unanswered = fields.Where(f => f.Required && string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Label).ToList();
        return this with
        {
            Fields = fields,
            UnansweredFields = unanswered,
            Status = unanswered.Count == 0 ? SubmissionStatus.PREPARED : SubmissionStatus.NEEDS_ANSWERS,
            ApprovalId = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public SubmissionPackage WithApproval(Guid approvalId) => this with { ApprovalId = approvalId, Status = SubmissionStatus.AWAITING_APPROVAL, UpdatedAt = DateTimeOffset.UtcNow };
    public SubmissionPackage MarkSubmitted(string? confirmation, string? screenshot) =>
        this with { Status = SubmissionStatus.SUBMITTED, ConfirmationText = confirmation, ScreenshotPath = screenshot ?? ScreenshotPath, UpdatedAt = DateTimeOffset.UtcNow };
    public SubmissionPackage MarkFailed(string reason) => this with { Status = SubmissionStatus.FAILED, ConfirmationText = reason, UpdatedAt = DateTimeOffset.UtcNow };
}

/// <summary>Per-site automation switch and daily submission cap.</summary>
public sealed record SiteSettings(string Adapter, bool Enabled, int DailyCap)
{
    public static readonly IReadOnlyList<string> KnownAdapters = ["company", "linkedin", "indeed"];

    public static IReadOnlyList<SiteSettings> Defaults() =>
        [new("company", true, 10), new("linkedin", false, 5), new("indeed", false, 5)];

    public static int SubmittedToday(IEnumerable<SubmissionPackage> packages, string adapter, DateTimeOffset now) =>
        packages.Count(p => p.Status == SubmissionStatus.SUBMITTED
            && string.Equals(p.Adapter, adapter, StringComparison.OrdinalIgnoreCase)
            && p.UpdatedAt.UtcDateTime.Date == now.UtcDateTime.Date);
}
