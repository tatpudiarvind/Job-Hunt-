namespace ArvindJobHunter.Application.Abstractions;

public sealed record JobSearchPreferences
{
    public IReadOnlyList<string> PreferredCountries { get; init; } = [];
    public string? LinkedInProfileUrl { get; init; }
    public string? ResumeLinkedInProfileUrl { get; init; }
    public IReadOnlyList<string> PreferredRoles { get; init; } = [];
    public string? WorkMode { get; init; }
    public string? SalaryExpectation { get; init; }
    public string? WorkAuthorization { get; init; }

    public string? EffectiveLinkedInProfileUrl =>
        !string.IsNullOrWhiteSpace(LinkedInProfileUrl)
            ? LinkedInProfileUrl
            : string.IsNullOrWhiteSpace(ResumeLinkedInProfileUrl) ? null : ResumeLinkedInProfileUrl;
}

public interface IJobSearchPreferencesProvider
{
    Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken);
}
