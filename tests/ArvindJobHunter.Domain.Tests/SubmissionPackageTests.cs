using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class SubmissionPackageTests
{
    internal static SubmissionPackage Package() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "https://jobs.example/1", "company", "r.pdf", "abc", "Dear team",
        [new("Email", "a@b.c", true, "profile"), new("Notice period", null, true, "unknown")],
        ["Notice period"], null, SubmissionStatus.NEEDS_ANSWERS, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public void Answer_FillsFieldsAndChangesPayload()
    {
        var package = Package().WithApproval(Guid.NewGuid());
        var answered = package.Answer(new Dictionary<string, string> { ["Notice period"] = "1 month" });

        Assert.True(answered.ReadyForApproval);
        Assert.Null(answered.ApprovalId);
        Assert.Equal(SubmissionStatus.PREPARED, answered.Status);
        Assert.NotEqual(package.PayloadForApproval(), answered.PayloadForApproval());
    }

    [Fact]
    public void Payload_ChangesWithResumeHash() =>
        Assert.NotEqual(Package().PayloadForApproval(), (Package() with { ResumeSha256 = "def" }).PayloadForApproval());

    [Fact]
    public void SubmittedToday_CountsOnlyTodayForAdapter()
    {
        var now = DateTimeOffset.UtcNow;
        var done = Package().MarkSubmitted("ok", null);
        var list = new[] { done, done with { Adapter = "linkedin" }, done with { UpdatedAt = now.AddDays(-2) }, Package() };
        Assert.Equal(1, SiteSettings.SubmittedToday(list, "company", now));
    }
}
