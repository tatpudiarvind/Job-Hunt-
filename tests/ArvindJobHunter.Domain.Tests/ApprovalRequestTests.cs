using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class ApprovalRequestTests
{
    private static ApprovalRequest Pending(string payload = "payload", TimeSpan? ttl = null) =>
        ApprovalRequest.Create(ApprovalActionType.SEND_EMAIL, "EmailDraft", Guid.NewGuid(), payload, "Send email", ExecutionMode.LIVE, LocalUser.Id, ttl ?? TimeSpan.FromHours(1));

    [Fact]
    public void Create_HashesPayloadAndStartsPending()
    {
        var approval = Pending("hello");
        Assert.Equal(ApprovalStatus.PENDING, approval.Status);
        Assert.Equal(ApprovalRequest.ComputeHash("hello"), approval.PayloadHash);
        Assert.NotEqual(ApprovalRequest.ComputeHash("hello!"), approval.PayloadHash);
    }

    [Fact]
    public void IsUsable_OnlyWhenApprovedUnexpiredSamePayloadActionAndMode()
    {
        var now = DateTimeOffset.UtcNow;
        var pending = Pending();
        Assert.False(pending.IsUsable(pending.PayloadHash, ApprovalActionType.SEND_EMAIL, ExecutionMode.LIVE, now));

        var approved = pending.Decide(true, LocalUser.Id, null, now);
        Assert.True(approved.IsUsable(approved.PayloadHash, ApprovalActionType.SEND_EMAIL, ExecutionMode.LIVE, now));
        Assert.False(approved.IsUsable(ApprovalRequest.ComputeHash("tampered"), ApprovalActionType.SEND_EMAIL, ExecutionMode.LIVE, now));
        Assert.False(approved.IsUsable(approved.PayloadHash, ApprovalActionType.CREATE_EMAIL_DRAFT, ExecutionMode.LIVE, now));
        Assert.False(approved.IsUsable(approved.PayloadHash, ApprovalActionType.SEND_EMAIL, ExecutionMode.DEMO, now));
        Assert.False(approved.IsUsable(approved.PayloadHash, ApprovalActionType.SEND_EMAIL, ExecutionMode.LIVE, now.AddHours(2)));
    }

    [Fact]
    public void Decide_Twice_Throws()
    {
        var approved = Pending().Decide(true, LocalUser.Id, null, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() => approved.Decide(false, LocalUser.Id, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Decide_AfterExpiry_BecomesExpiredNotApproved()
    {
        var approval = Pending(ttl: TimeSpan.FromMilliseconds(1));
        var decided = approval.Decide(true, LocalUser.Id, null, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(ApprovalStatus.EXPIRED, decided.Status);
    }

    [Fact]
    public void Consume_RequiresApprovedAndIsOneShot()
    {
        var pending = Pending();
        Assert.Throws<InvalidOperationException>(() => pending.Consume(DateTimeOffset.UtcNow));

        var consumed = pending.Decide(true, LocalUser.Id, null, DateTimeOffset.UtcNow).Consume(DateTimeOffset.UtcNow);
        Assert.Equal(ApprovalStatus.CONSUMED, consumed.Status);
        Assert.NotNull(consumed.ConsumedAt);
        Assert.Throws<InvalidOperationException>(() => consumed.Consume(DateTimeOffset.UtcNow));
    }
}
