using System.Security.Cryptography;
using System.Text;

namespace ArvindJobHunter.Domain.Entities;

public sealed record ApprovalRequest(
    Guid Id,
    ApprovalActionType ActionType,
    string TargetType,
    Guid TargetId,
    string PayloadHash,
    string Summary,
    ExecutionMode Mode,
    ApprovalStatus Status,
    Guid RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset ExpiresAt,
    Guid? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? ConsumedAt)
{
    public static ApprovalRequest Create(ApprovalActionType actionType, string targetType, Guid targetId, string payload, string summary, ExecutionMode mode, Guid requestedBy, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        return new ApprovalRequest(Guid.NewGuid(), actionType, targetType, targetId, ComputeHash(payload), summary, mode, ApprovalStatus.PENDING, requestedBy, now, now.Add(ttl), null, null, null, null);
    }

    public static string ComputeHash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public bool IsUsable(string payloadHash, ApprovalActionType action, ExecutionMode mode, DateTimeOffset now) =>
        Status == ApprovalStatus.APPROVED
        && !IsExpired(now)
        && string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal)
        && ActionType == action
        && Mode == mode;

    public ApprovalRequest Decide(bool approved, Guid decidedBy, string? note, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.PENDING) throw new InvalidOperationException($"Approval is already {Status}.");
        if (IsExpired(now)) return this with { Status = ApprovalStatus.EXPIRED };
        return this with
        {
            Status = approved ? ApprovalStatus.APPROVED : ApprovalStatus.REJECTED,
            DecidedBy = decidedBy,
            DecidedAt = now,
            DecisionNote = note
        };
    }

    public ApprovalRequest Invalidate(string reason) =>
        Status is ApprovalStatus.PENDING or ApprovalStatus.APPROVED
            ? this with { Status = ApprovalStatus.INVALIDATED, DecisionNote = reason }
            : this;

    public ApprovalRequest Consume(DateTimeOffset now)
    {
        if (Status != ApprovalStatus.APPROVED) throw new InvalidOperationException("Only approved requests can be consumed.");
        return this with { Status = ApprovalStatus.CONSUMED, ConsumedAt = now };
    }
}
