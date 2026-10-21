using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

public sealed record JobDiscoveryRequest(
    string? CurrentRole,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Preferences,
    IReadOnlyList<string> PreferredRoles,
    IReadOnlyList<string> TargetRegions,
    string? WorkMode,
    string? LinkedInProfileUrl,
    bool RequireVisaSponsorship,
    int MaxResults);

public sealed record DiscoveredJobPosting(
    string ExternalId,
    string Title,
    string Company,
    string Location,
    string Source,
    string Url,
    string Description,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PostedAt = null);

public sealed record RankedDiscoveredJob(
    DiscoveredJobPosting Posting,
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> Reasons);

public sealed record JobDiscoveryResult(
    IReadOnlyList<Job> Jobs,
    int Imported,
    int Updated,
    int Skipped,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Notes);

public interface IJobDiscoverySource
{
    string Name { get; }
    Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken);
}

public sealed record SkillDemand(string Skill, int Postings, int PercentOfPostings, bool OnResume);

public sealed record CountrySkillDemand(string Country, int Postings, IReadOnlyList<SkillDemand> TopSkills);

public sealed record SkillDemandReport(
    int SponsoredPostings,
    int PostingsMatchingResume,
    IReadOnlyList<string> ResumeSkills,
    IReadOnlyList<SkillDemand> Skills,
    IReadOnlyList<CountrySkillDemand> Countries);
