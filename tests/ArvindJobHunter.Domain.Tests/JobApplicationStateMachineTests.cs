using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class JobApplicationStateMachineTests
{
    [Theory]
    [InlineData(ApplicationStatus.DISCOVERED, ApplicationStatus.MATCHED)]
    [InlineData(ApplicationStatus.MATCHED, ApplicationStatus.SHORTLISTED)]
    [InlineData(ApplicationStatus.SHORTLISTED, ApplicationStatus.RESUME_PREPARED)]
    [InlineData(ApplicationStatus.RESUME_PREPARED, ApplicationStatus.AWAITING_APPROVAL)]
    [InlineData(ApplicationStatus.AWAITING_APPROVAL, ApplicationStatus.APPROVED)]
    [InlineData(ApplicationStatus.APPROVED, ApplicationStatus.APPLIED)]
    [InlineData(ApplicationStatus.INTERVIEW, ApplicationStatus.OFFER)]
    public void CanTransition_AllowsHappyPath(ApplicationStatus from, ApplicationStatus to)
        => Assert.True(JobApplication.CanTransition(from, to));

    [Theory]
    [InlineData(ApplicationStatus.DISCOVERED, ApplicationStatus.APPLIED)]
    [InlineData(ApplicationStatus.MATCHED, ApplicationStatus.APPROVED)]
    [InlineData(ApplicationStatus.SHORTLISTED, ApplicationStatus.APPLIED)]
    [InlineData(ApplicationStatus.AWAITING_APPROVAL, ApplicationStatus.APPLIED)]
    [InlineData(ApplicationStatus.CLOSED, ApplicationStatus.DISCOVERED)]
    [InlineData(ApplicationStatus.REJECTED, ApplicationStatus.OFFER)]
    public void CanTransition_RejectsSkippingApprovalOrReopening(ApplicationStatus from, ApplicationStatus to)
        => Assert.False(JobApplication.CanTransition(from, to));

    [Fact]
    public void TransitionTo_AppendsHistoryAndUpdatesStatus()
    {
        var app = JobApplication.Create(Guid.NewGuid());

        var moved = app.TransitionTo(ApplicationStatus.MATCHED, "Score 90");

        Assert.Equal(ApplicationStatus.MATCHED, moved.Status);
        Assert.Equal(2, moved.History.Count);
        Assert.Equal(ApplicationStatus.DISCOVERED, moved.History[^1].From);
        Assert.Equal("Score 90", moved.History[^1].Reason);
        Assert.Equal(ApplicationStatus.DISCOVERED, app.Status);
    }

    [Fact]
    public void TransitionTo_InvalidThrows()
    {
        var app = JobApplication.Create(Guid.NewGuid());
        var ex = Assert.Throws<InvalidOperationException>(() => app.TransitionTo(ApplicationStatus.APPLIED, "skip"));
        Assert.Contains("cannot move", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryStatusExceptClosed_HasAtLeastOneExit()
    {
        foreach (var status in Enum.GetValues<ApplicationStatus>().Where(s => s != ApplicationStatus.CLOSED))
        {
            Assert.Contains(Enum.GetValues<ApplicationStatus>(), to => JobApplication.CanTransition(status, to));
        }
    }
}
