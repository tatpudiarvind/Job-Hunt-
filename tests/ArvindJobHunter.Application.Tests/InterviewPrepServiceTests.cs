using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain.Entities;
using Xunit;

namespace ArvindJobHunter.Application.Tests;

public sealed class InterviewPrepServiceTests
{
    private static Job AnalyzedJob() => Job.Create("Senior .NET Engineer", "Contoso", "Pune", "MANUAL", null, "desc")
        .WithAnalysis(new JobAnalysis(["C#", "ASP.NET Core", "Azure"], ["Kafka"], "Senior", "s", [], DateTimeOffset.UtcNow))
        .WithMatch(new JobMatch(70, ["C#", "ASP.NET Core"], ["Azure"], "r", DateTimeOffset.UtcNow), 60);

    [Fact]
    public void Build_PrioritizesGaps_AndEmitsOnlyAbsoluteHttpsLinks()
    {
        var plan = new InterviewPrepService().Build(AnalyzedJob());

        Assert.Equal("Azure", plan.FocusSkills[0]);
        Assert.Contains(plan.Groups, g => g.Topic == "Azure (gap)");
        var all = plan.Groups.SelectMany(g => g.Resources).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, r => Assert.True(Uri.TryCreate(r.Url, UriKind.Absolute, out var u) && u.Scheme == "https", r.Url));
        Assert.Contains(all, r => r.Provider == "YouTube" && r.Kind == "video" && r.Url.StartsWith("https://www.youtube.com/results?search_query="));
        Assert.Contains(all, r => r.Provider == "Microsoft Learn");
    }

    [Fact]
    public void Build_WorksWithoutAnalysis()
    {
        var plan = new InterviewPrepService().Build(Job.Create("QA Lead", "Fabrikam", "", "MANUAL", null, "d"));
        Assert.Empty(plan.FocusSkills);
        Assert.Contains(plan.Groups, g => g.Topic == "Company research");
        Assert.Contains(plan.Groups, g => g.Topic == "Role & behavioral");
        Assert.Contains(plan.Groups, g => g.Topic == "Practice & system design");
    }

    [Fact]
    public void Build_UrlEncodesQueries()
    {
        var plan = new InterviewPrepService().Build(Job.Create("C# Dev", "A & B Ltd", "", "MANUAL", null, "d"));
        var url = plan.Groups.First(g => g.Topic == "Company research").Resources[0].Url;
        Assert.DoesNotContain(" ", url);
        Assert.DoesNotContain("&amp;", url);
        Assert.Contains("A+%26+B", url);
    }
}
