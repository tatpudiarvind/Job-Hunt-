using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Tests;

public sealed class ExternalActionGuardTests
{
    private readonly ExternalActionGuard guard = new();

    private static ApprovalRequest Approved(ApprovalActionType action = ApprovalActionType.SEND_EMAIL, ExecutionMode mode = ExecutionMode.LIVE, string payload = "p") =>
        ApprovalRequest.Create(action, "EmailDraft", Guid.NewGuid(), payload, "s", mode, LocalUser.Id, TimeSpan.FromHours(1))
            .Decide(true, LocalUser.Id, null, DateTimeOffset.UtcNow);

    [Fact]
    public void Allows_ValidApprovedMatchingRequest()
    {
        var approval = Approved();
        guard.EnsureAllowed(approval, ApprovalActionType.SEND_EMAIL, ApprovalRequest.ComputeHash("p"), ExecutionMode.LIVE);
    }

    [Fact]
    public void Blocks_PendingApproval()
    {
        var pending = ApprovalRequest.Create(ApprovalActionType.SEND_EMAIL, "EmailDraft", Guid.NewGuid(), "p", "s", ExecutionMode.LIVE, LocalUser.Id, TimeSpan.FromHours(1));
        var ex = Assert.Throws<ApprovalViolationException>(() => guard.EnsureAllowed(pending, ApprovalActionType.SEND_EMAIL, pending.PayloadHash, ExecutionMode.LIVE));
        Assert.Contains("not APPROVED", ex.Message);
    }

    [Fact]
    public void Blocks_PayloadChangedAfterApproval()
    {
        var approval = Approved();
        var ex = Assert.Throws<ApprovalViolationException>(() => guard.EnsureAllowed(approval, ApprovalActionType.SEND_EMAIL, ApprovalRequest.ComputeHash("edited"), ExecutionMode.LIVE));
        Assert.Contains("payload changed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Blocks_ActionTypeMismatch()
    {
        var approval = Approved(ApprovalActionType.CREATE_EMAIL_DRAFT);
        Assert.Throws<ApprovalViolationException>(() => guard.EnsureAllowed(approval, ApprovalActionType.SEND_EMAIL, approval.PayloadHash, ExecutionMode.LIVE));
    }

    [Fact]
    public void Blocks_ModeMismatch_DemoApprovalCannotRunLive()
    {
        var approval = Approved(mode: ExecutionMode.DEMO);
        var ex = Assert.Throws<ApprovalViolationException>(() => guard.EnsureAllowed(approval, ApprovalActionType.SEND_EMAIL, approval.PayloadHash, ExecutionMode.LIVE));
        Assert.Contains("DEMO", ex.Message);
    }

    [Fact]
    public void Blocks_SubmitApplication_InLive()
    {
        var approval = Approved(ApprovalActionType.SUBMIT_APPLICATION);
        var ex = Assert.Throws<ApprovalViolationException>(() => guard.EnsureAllowed(approval, ApprovalActionType.SUBMIT_APPLICATION, approval.PayloadHash, ExecutionMode.LIVE));
        Assert.Contains("not enabled", ex.Message);
    }
}
