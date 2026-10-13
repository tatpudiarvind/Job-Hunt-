using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Persistence;

public sealed class JsonJobSearchPreferencesProvider(IJsonStore<JobSearchPreferences> store) : IJobSearchPreferencesProvider, ICacheInvalidatable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private JobSearchPreferences? cache;

    public void Invalidate() { gate.Wait(); try { cache = null; } finally { gate.Release(); } }

    public async Task<JobSearchPreferences> GetAsync(CancellationToken cancellationToken)
    {
        if (cache is not null) return cache;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache is not null) return cache;
            cache = Normalize(await store.LoadAsync(cancellationToken));
            return cache;
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(JobSearchPreferences preferences, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            cache = Normalize(preferences);
            await store.SaveAsync(cache, cancellationToken);
        }
        finally { gate.Release(); }
    }

    private static JobSearchPreferences Normalize(JobSearchPreferences preferences) => new()
    {
        PreferredCountries = CleanList(preferences.PreferredCountries),
        LinkedInProfileUrl = CleanOptional(preferences.LinkedInProfileUrl),
        ResumeLinkedInProfileUrl = CleanOptional(preferences.ResumeLinkedInProfileUrl),
        PreferredRoles = CleanList(preferences.PreferredRoles),
        WorkMode = CleanOptional(preferences.WorkMode),
        SalaryExpectation = CleanOptional(preferences.SalaryExpectation),
        WorkAuthorization = CleanOptional(preferences.WorkAuthorization)
    };

    private static IReadOnlyList<string> CleanList(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
