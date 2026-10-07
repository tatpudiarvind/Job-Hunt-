using System.Net;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

public sealed record InterviewResource(string Title, string Url, string Provider, string Kind, string? Why);

public sealed record InterviewResourceGroup(string Topic, IReadOnlyList<InterviewResource> Resources);

public sealed record InterviewPrepPlan(
    Guid JobId,
    string JobTitle,
    string Company,
    IReadOnlyList<string> FocusSkills,
    IReadOnlyList<InterviewResourceGroup> Groups,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Builds outbound interview-prep links (YouTube searches, documentation, practice sites, company research).
/// Deterministic and offline: it only composes URLs; nothing is fetched, embedded, or persisted.
/// </summary>
public sealed class InterviewPrepService
{
    private const int MaxSkills = 6;

    public InterviewPrepPlan Build(Job job)
    {
        var missing = job.Match?.MissingSkills ?? [];
        var required = job.Analysis?.RequiredSkills ?? [];
        var matched = job.Match?.MatchedSkills ?? [];

        var focus = missing.Concat(required).Concat(matched)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSkills)
            .ToList();

        var groups = new List<InterviewResourceGroup>
        {
            new("Company research", CompanyLinks(job)),
            new("Role & behavioral", RoleLinks(job))
        };

        foreach (var skill in focus)
        {
            var isGap = missing.Contains(skill, StringComparer.OrdinalIgnoreCase);
            groups.Add(new InterviewResourceGroup(isGap ? $"{skill} (gap)" : skill, SkillLinks(skill, isGap)));
        }

        groups.Add(new InterviewResourceGroup("Practice & system design", GeneralLinks(job)));

        return new InterviewPrepPlan(job.Id, job.Title, job.Company, focus, groups, DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<InterviewResource> CompanyLinks(Job job) =>
    [
        new($"{job.Company} interview experiences on Glassdoor", Google($"{job.Company} interview questions site:glassdoor.com"), "Google", "search", "Real candidate reports for this company."),
        new($"{job.Company} on LinkedIn", $"https://www.linkedin.com/search/results/companies/?keywords={Enc(job.Company)}", "LinkedIn", "website", "Company page, people, and recent posts."),
        new($"{job.Company} recent news", Google($"{job.Company} news"), "Google", "search", "Talking points for 'why us' questions."),
        new($"{job.Company} {job.Title} interview process", YouTube($"{job.Company} {job.Title} interview"), "YouTube", "video", "Walkthroughs from candidates and recruiters.")
    ];

    private static IReadOnlyList<InterviewResource> RoleLinks(Job job)
    {
        var level = job.Analysis?.SeniorityLevel is { Length: > 0 } s ? s : "";
        var role = $"{level} {job.Title}".Trim();
        return
        [
            new($"{role} interview questions", YouTube($"{role} interview questions and answers"), "YouTube", "video", null),
            new($"{role} mock interview", YouTube($"{role} mock interview"), "YouTube", "video", "Watch a full mock to calibrate expectations."),
            new("Behavioral questions with the STAR method", YouTube("STAR method behavioral interview questions"), "YouTube", "video", null),
            new($"{job.Title} interview guide", Google($"{job.Title} interview guide"), "Google", "search", null)
        ];
    }

    private static IReadOnlyList<InterviewResource> SkillLinks(string skill, bool isGap)
    {
        var list = new List<InterviewResource>
        {
            new($"{skill} interview questions", YouTube($"{skill} interview questions"), "YouTube", "video", null),
            new($"{skill} crash course", YouTube($"{skill} tutorial for beginners full course"), "YouTube", "video", isGap ? "Start here to close the gap." : null),
            new($"{skill} official docs", Google($"{skill} official documentation"), "Google", "search", null),
            new($"{skill} on GeeksforGeeks", $"https://www.geeksforgeeks.org/search/?q={Enc(skill)}", "GeeksforGeeks", "website", null)
        };
        if (IsMicrosoftStack(skill))
        {
            list.Add(new($"{skill} on Microsoft Learn", $"https://learn.microsoft.com/search/?terms={Enc(skill)}", "Microsoft Learn", "website", "Authoritative reference and tutorials."));
        }
        return list;
    }

    private static IReadOnlyList<InterviewResource> GeneralLinks(Job job) =>
    [
        new("LeetCode problems", "https://leetcode.com/problemset/", "LeetCode", "website", "Coding practice by topic and difficulty."),
        new("HackerRank interview preparation kit", "https://www.hackerrank.com/interview/interview-preparation-kit", "HackerRank", "website", null),
        new("System design interview primer", "https://github.com/donnemartin/system-design-primer", "GitHub", "website", "Widely used open reference."),
        new("System design interview walkthrough", YouTube("system design interview walkthrough"), "YouTube", "video", null),
        new($"Salary research for {job.Title}", Google($"{job.Title} salary {job.Location}".Trim()), "Google", "search", "Prepare for compensation discussions.")
    ];

    private static bool IsMicrosoftStack(string skill) =>
        skill.Contains(".NET", StringComparison.OrdinalIgnoreCase) || skill.Contains("C#", StringComparison.OrdinalIgnoreCase) ||
        skill.Contains("Azure", StringComparison.OrdinalIgnoreCase) || skill.Contains("ASP.NET", StringComparison.OrdinalIgnoreCase) ||
        skill.Contains("SQL Server", StringComparison.OrdinalIgnoreCase) || skill.Contains("Entity Framework", StringComparison.OrdinalIgnoreCase) ||
        skill.Contains("Blazor", StringComparison.OrdinalIgnoreCase) || skill.Contains("PowerShell", StringComparison.OrdinalIgnoreCase);

    private static string YouTube(string query) => $"https://www.youtube.com/results?search_query={Enc(query)}";
    private static string Google(string query) => $"https://www.google.com/search?q={Enc(query)}";
    private static string Enc(string value) => WebUtility.UrlEncode(value.Trim());
}
