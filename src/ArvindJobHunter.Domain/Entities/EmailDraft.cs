namespace ArvindJobHunter.Domain.Entities;

public sealed record EmailDraft(
    Guid Id,
    Guid? JobId,
    string Kind,
    string To,
    string Subject,
    string Body,
    EmailDraftStatus Status,
    Guid? ApprovalId,
    string? ExternalId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static EmailDraft Create(Guid? jobId, string kind, string to, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject is required.", nameof(subject));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Body is required.", nameof(body));
        var now = DateTimeOffset.UtcNow;
        return new EmailDraft(Guid.NewGuid(), jobId, kind, to.Trim(), subject.Trim(), body.Trim(), EmailDraftStatus.DRAFT, null, null, now, now);
    }

    public string PayloadForApproval() => $"{To}|{Subject}|{Body}";

    public EmailDraft Edit(string to, string subject, string body) =>
        this with { To = to.Trim(), Subject = subject.Trim(), Body = body.Trim(), Status = EmailDraftStatus.DRAFT, ApprovalId = null, UpdatedAt = DateTimeOffset.UtcNow };

    public EmailDraft WithApproval(Guid approvalId) => this with { ApprovalId = approvalId, UpdatedAt = DateTimeOffset.UtcNow };

    public EmailDraft MarkApproved() => this with { Status = EmailDraftStatus.APPROVED, UpdatedAt = DateTimeOffset.UtcNow };

    public EmailDraft MarkCreatedInGmail(string externalId) =>
        this with { Status = EmailDraftStatus.CREATED_IN_GMAIL, ExternalId = externalId, UpdatedAt = DateTimeOffset.UtcNow };

    public EmailDraft MarkSent(string externalId) =>
        this with { Status = EmailDraftStatus.SENT, ExternalId = externalId, UpdatedAt = DateTimeOffset.UtcNow };

    public EmailDraft MarkFailed() => this with { Status = EmailDraftStatus.FAILED, UpdatedAt = DateTimeOffset.UtcNow };
}
