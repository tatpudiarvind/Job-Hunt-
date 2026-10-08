using System.Net.Mail;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class EmailDraftService(IRepository<EmailDraft> repository, ApprovalService approvals, AuditService audit)
{
    public async Task<IReadOnlyList<EmailDraft>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).OrderByDescending(d => d.UpdatedAt).ToList();

    public Task<EmailDraft?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<EmailDraft> CreateAsync(Guid? jobId, string kind, string to, string subject, string body, Guid userId, CancellationToken cancellationToken)
    {
        var draft = EmailDraft.Create(jobId, kind, to, subject, body);
        await repository.UpsertAsync(draft, cancellationToken);
        await audit.RecordAsync(userId, "EMAIL_DRAFT_CREATED", "EmailDraft", draft.Id.ToString(), "DRAFT", $"{kind}: {subject}", cancellationToken);
        return draft;
    }

    public async Task<EmailDraft?> EditAsync(Guid id, string to, string subject, string body, Guid userId, CancellationToken cancellationToken)
    {
        var draft = await repository.GetAsync(id, cancellationToken);
        if (draft is null) return null;
        var edited = draft.Edit(to, subject, body);
        await repository.UpsertAsync(edited, cancellationToken);
        await approvals.InvalidateForTargetAsync(id, "Email draft content changed.", userId, cancellationToken);
        await audit.RecordAsync(userId, "EMAIL_DRAFT_EDITED", "EmailDraft", id.ToString(), "DRAFT", null, cancellationToken);
        return edited;
    }

    public async Task<ApprovalRequest?> RequestApprovalAsync(Guid id, bool send, Guid userId, CancellationToken cancellationToken)
    {
        var draft = await repository.GetAsync(id, cancellationToken);
        if (draft is null) return null;
        // A valid address is required to send; drafts may stay unaddressed, but whatever is there must parse,
        // because the value is written verbatim into the MIME "To:" header.
        if (send ? !HasValidRecipients(draft.To) : !string.IsNullOrWhiteSpace(draft.To) && !HasValidRecipients(draft.To))
        {
            throw new ArgumentException(send
                ? "Add a valid recipient email address before requesting approval to send."
                : "The recipient is not a valid email address.");
        }

        var action = send ? ApprovalActionType.SEND_EMAIL : ApprovalActionType.CREATE_EMAIL_DRAFT;
        var approval = await approvals.RequestAsync(action, nameof(EmailDraft), draft.Id, draft.PayloadForApproval(),
            $"{(send ? "Send" : "Create Gmail draft")} \"{draft.Subject}\" to {draft.To}", userId, cancellationToken);
        await repository.UpsertAsync(draft.WithApproval(approval.Id), cancellationToken);
        return approval;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var deleted = await repository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            await approvals.InvalidateForTargetAsync(id, "Email draft deleted.", userId, cancellationToken);
            await audit.RecordAsync(userId, "EMAIL_DRAFT_DELETED", "EmailDraft", id.ToString(), "SUCCESS", null, cancellationToken);
        }

        return deleted;
    }

    public Task SaveAsync(EmailDraft draft, CancellationToken cancellationToken) => repository.UpsertAsync(draft, cancellationToken);

    /// <summary>One or more comma/semicolon separated addresses, each optionally with a display name.</summary>
    public static bool HasValidRecipients(string? to)
    {
        if (string.IsNullOrWhiteSpace(to)) return false;
        var parts = to.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(part => !part.Any(char.IsControl) && MailAddress.TryCreate(part, out _));
    }
}
