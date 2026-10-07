using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class ApprovalService(IRepository<ApprovalRequest> repository, IRuntimeSettingsProvider settings, AuditService audit)
{
    public async Task<IReadOnlyList<ApprovalRequest>> ListAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var items = await repository.ListAsync(cancellationToken);
        var result = new List<ApprovalRequest>(items.Count);
        foreach (var item in items)
        {
            if (item.Status == ApprovalStatus.PENDING && item.IsExpired(now))
            {
                var expired = item with { Status = ApprovalStatus.EXPIRED };
                await repository.UpsertAsync(expired, cancellationToken);
                result.Add(expired);
            }
            else
            {
                result.Add(item);
            }
        }

        return result.OrderByDescending(a => a.RequestedAt).ToList();
    }

    public Task<ApprovalRequest?> GetAsync(Guid id, CancellationToken cancellationToken) => repository.GetAsync(id, cancellationToken);

    public async Task<ApprovalRequest> RequestAsync(ApprovalActionType action, string targetType, Guid targetId, string payload, string summary, Guid userId, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        foreach (var previous in (await repository.ListAsync(cancellationToken)).Where(a => a.TargetId == targetId && a.ActionType == action && a.Status is ApprovalStatus.PENDING or ApprovalStatus.APPROVED))
        {
            await repository.UpsertAsync(previous.Invalidate("Superseded by a new approval request."), cancellationToken);
        }

        var approval = ApprovalRequest.Create(action, targetType, targetId, payload, summary, current.Mode, userId, TimeSpan.FromHours(current.ApprovalTtlHours));
        await repository.UpsertAsync(approval, cancellationToken);
        await audit.RecordAsync(userId, "APPROVAL_REQUESTED", "ApprovalRequest", approval.Id.ToString(), "PENDING", $"{action} {targetType}:{targetId}", cancellationToken);
        return approval;
    }

    public async Task<ApprovalRequest?> DecideAsync(Guid id, bool approved, string? note, Guid userId, CancellationToken cancellationToken)
    {
        var approval = await repository.GetAsync(id, cancellationToken);
        if (approval is null) return null;
        var decided = approval.Decide(approved, userId, note, DateTimeOffset.UtcNow);
        await repository.UpsertAsync(decided, cancellationToken);
        await audit.RecordAsync(userId, $"APPROVAL_{decided.Status}", "ApprovalRequest", id.ToString(), decided.Status.ToString(), note, cancellationToken);
        return decided;
    }

    public async Task InvalidateForTargetAsync(Guid targetId, string reason, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var approval in (await repository.ListAsync(cancellationToken)).Where(a => a.TargetId == targetId && a.Status is ApprovalStatus.PENDING or ApprovalStatus.APPROVED))
        {
            await repository.UpsertAsync(approval.Invalidate(reason), cancellationToken);
            await audit.RecordAsync(userId, "APPROVAL_INVALIDATED", "ApprovalRequest", approval.Id.ToString(), "INVALIDATED", reason, cancellationToken);
        }
    }

    public async Task ConsumeAsync(ApprovalRequest approval, CancellationToken cancellationToken) =>
        await repository.UpsertAsync(approval.Consume(DateTimeOffset.UtcNow), cancellationToken);
}
