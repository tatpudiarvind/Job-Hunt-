using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Xunit;

namespace ArvindJobHunter.Domain.Tests;

public sealed class JobApplicationFollowUpTests
{
    [Fact]
    public void ScheduleFollowUp_SetsDueAndTrimsNote()
    {
        var due = DateTimeOffset.UtcNow.AddDays(7);
        var app = JobApplication.Create(Guid.NewGuid()).ScheduleFollowUp(due, "  check in  ");
        Assert.Equal(due, app.FollowUpDueAt);
        Assert.Equal("check in", app.FollowUpNote);
    }

    [Fact]
    public void IsFollowUpDue_RequiresEligibleStatusAndPastDue()
    {
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        var discovered = JobApplication.Create(Guid.NewGuid()).ScheduleFollowUp(past, null);
        Assert.False(discovered.IsFollowUpDue(DateTimeOffset.UtcNow));

        var applied = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPLIED).ScheduleFollowUp(past, null);
        Assert.True(applied.IsFollowUpDue(DateTimeOffset.UtcNow));
        Assert.False(applied.IsFollowUpDue(past.AddHours(-1)));
    }

    [Fact]
    public void ClearFollowUp_RemovesReminder()
    {
        var app = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPLIED).ScheduleFollowUp(DateTimeOffset.UtcNow, "x").ClearFollowUp();
        Assert.Null(app.FollowUpDueAt);
        Assert.Null(app.FollowUpNote);
    }
}
