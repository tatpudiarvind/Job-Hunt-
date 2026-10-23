using ArvindJobHunter.Worker.Browser;

namespace ArvindJobHunter.Worker.Tests;

public sealed class JobLinkFilterTests
{
    private const string Page = "https://www.example-jobs.com/search?q=dotnet";

    [Fact]
    public void Filter_KeepsJobPostingLinks_ResolvesRelativeAndDeduplicates()
    {
        var links = JobLinkFilter.Filter(Page,
        [
            new RawLink("Senior .NET Engineer", "/jobs/123-senior-net-engineer"),
            new RawLink("Senior .NET Engineer", "/jobs/123-senior-net-engineer#apply"),
            new RawLink("Backend Developer", "https://www.example-jobs.com/viewjob?jk=abc"),
            new RawLink("  Cloud\n  Engineer ", "https://careers.contoso.com/careers/456")
        ]);

        Assert.Equal(3, links.Count);
        Assert.Equal("https://www.example-jobs.com/jobs/123-senior-net-engineer", links[0].Url);
        Assert.Equal("Cloud Engineer", links[2].Text);
    }

    [Fact]
    public void Filter_DropsNavigationAndNonJobLinks()
    {
        var links = JobLinkFilter.Filter(Page,
        [
            new RawLink("Sign in", "/jobs/login"),
            new RawLink("Next", "/jobs?page=2"),
            new RawLink("2", "/jobs?page=2"),
            new RawLink("About us", "/about"),
            new RawLink("Mail", "mailto:hr@example.com"),
            new RawLink("Script", "javascript:void(0)"),
            new RawLink("Privacy policy", "/privacy")
        ]);

        Assert.Empty(links);
    }

    [Fact]
    public void Filter_CapsResults() =>
        Assert.Equal(JobLinkFilter.MaxLinks,
            JobLinkFilter.Filter(Page, Enumerable.Range(0, 250).Select(i => new RawLink($"Role {i}", $"/jobs/{i}"))).Count);

    [Fact]
    public void Filter_InvalidPageUrl_ReturnsNothing() =>
        Assert.Empty(JobLinkFilter.Filter("not a url", [new RawLink("Role", "/jobs/1")]));
}
