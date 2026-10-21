using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;
using Xunit;

namespace ArvindJobHunter.Application.Tests;

public sealed class FollowUpDraftServiceTests
{
    [Fact]
    public void Compose_MentionsRoleCompanyAndCandidate()
    {
        var job = Job.Create("Engineer", "Contoso", "Berlin", "MANUAL", null, "C# role");
        var (subject, body) = FollowUpDraftService.Compose(job, JobApplication.Create(job.Id), "Arvind");

        Assert.Contains("Engineer", subject);
        Assert.Contains("Contoso", body);
        Assert.Contains("Arvind", body);
        Assert.StartsWith("Hello,", body);
    }

    [Fact]
    public void Compose_GreetsRecruiterByName()
    {
        var contact = RecruiterContact.Create(RecruiterContactStatus.USER_PROVIDED, "Sam", "sam@contoso.com", "careers page");
        var job = Job.Create("Engineer", "Contoso", "Berlin", "MANUAL", null, "C# role") with { Recruiter = contact };
        var (_, body) = FollowUpDraftService.Compose(job, JobApplication.Create(job.Id), null);
        Assert.StartsWith("Hello Sam,", body);
    }
}
