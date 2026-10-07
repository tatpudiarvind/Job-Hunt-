using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed class ExternalActionGuard : IExternalActionGuard
{
    public void EnsureAllowed(ApprovalRequest approval, ApprovalActionType action, string payloadHash, ExecutionMode currentMode)
    {
        var now = DateTimeOffset.UtcNow;
        if (approval.Status != ApprovalStatus.APPROVED)
            throw new ApprovalViolationException($"Approval {approval.Id} is {approval.Status}, not APPROVED.");
        if (approval.IsExpired(now))
            throw new ApprovalViolationException($"Approval {approval.Id} expired at {approval.ExpiresAt:O}.");
        if (approval.ActionType != action)
            throw new ApprovalViolationException($"Approval {approval.Id} is for {approval.ActionType}, not {action}.");
        if (!string.Equals(approval.PayloadHash, payloadHash, StringComparison.Ordinal))
            throw new ApprovalViolationException("The payload changed after approval. Request a new approval.");
        if (approval.Mode != currentMode)
            throw new ApprovalViolationException($"Approval was granted for {approval.Mode} mode but the system is in {currentMode} mode.");
        if (currentMode == ExecutionMode.LIVE && action is not (ApprovalActionType.CREATE_EMAIL_DRAFT or ApprovalActionType.APPLY_RESUME_CHANGES or ApprovalActionType.SEND_EMAIL))
            throw new ApprovalViolationException($"{action} is not enabled for LIVE execution in this build.");
    }
}

public sealed class ApprovalViolationException(string message) : InvalidOperationException(message);
