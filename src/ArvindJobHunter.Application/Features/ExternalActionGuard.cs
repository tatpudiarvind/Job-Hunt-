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
        if (currentMode == ExecutionMode.LIVE && action is not (ApprovalActionType.CREATE_EMAIL_DRAFT or ApprovalActionType.APPLY_RESUME_CHANGES or ApprovalActionType.SEND_EMAIL or ApprovalActionType.GENERATE_COVER_LETTER or ApprovalActionType.SUBMIT_APPLICATION))
            throw new ApprovalViolationException($"{action} is not enabled for LIVE execution in this build.");
    }

    /// <summary>A LIVE submission also needs the site's adapter switched on and its daily cap not reached.</summary>
    public static void EnsureSiteAllowed(string adapter, IReadOnlyList<SiteSettings> sites, int submittedToday)
    {
        var site = sites.FirstOrDefault(s => string.Equals(s.Adapter, adapter, StringComparison.OrdinalIgnoreCase));
        if (site is null || !site.Enabled)
            throw new ApprovalViolationException($"Auto-apply on {adapter} is switched off in Settings.");
        if (submittedToday >= site.DailyCap)
            throw new ApprovalViolationException($"Daily cap reached for {adapter}: {submittedToday}/{site.DailyCap} submitted today.");
    }
}

public sealed class ApprovalViolationException(string message) : InvalidOperationException(message);
