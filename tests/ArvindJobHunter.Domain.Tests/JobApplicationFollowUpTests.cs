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

    [Fact]
    public void Applying_SchedulesDefaultReminderAfterSevenDays()
    {
        var before = DateTimeOffset.UtcNow;
        var app = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPROVED).TransitionTo(ApplicationStatus.APPLIED, "submitted");
        Assert.Equal(JobApplication.AutoFollowUpNote, app.FollowUpNote);
        Assert.InRange(app.FollowUpDueAt!.Value, before.AddDays(7), DateTimeOffset.UtcNow.AddDays(7));
        Assert.False(app.IsFollowUpDue(DateTimeOffset.UtcNow));
        Assert.True(app.IsFollowUpDue(DateTimeOffset.UtcNow.AddDays(8)));
    }

    [Fact]
    public void Applying_KeepsManuallyScheduledReminder()
    {
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var app = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPROVED).ScheduleFollowUp(due, "mine").TransitionTo(ApplicationStatus.APPLIED, "submitted");
        Assert.Equal(due, app.FollowUpDueAt);
        Assert.Equal("mine", app.FollowUpNote);
    }

    [Fact]
    public void RecruiterReply_ClearsOnlyTheAutomaticReminder()
    {
        var auto = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPROVED).TransitionTo(ApplicationStatus.APPLIED, "s").TransitionTo(ApplicationStatus.RECRUITER_REPLIED, "r");
        Assert.Null(auto.FollowUpDueAt);

        var manual = JobApplication.Create(Guid.NewGuid(), ApplicationStatus.APPLIED).ScheduleFollowUp(DateTimeOffset.UtcNow.AddDays(2), "mine").TransitionTo(ApplicationStatus.RECRUITER_REPLIED, "r");
        Assert.NotNull(manual.FollowUpDueAt);
    }
}
