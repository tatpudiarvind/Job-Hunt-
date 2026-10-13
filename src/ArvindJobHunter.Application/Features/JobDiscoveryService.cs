using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed class JobDiscoveryService(
    IEnumerable<IJobDiscoverySource> sources,
    CandidateProfileService profiles,
    CandidateFactService facts,
    IJobSearchPreferencesProvider preferences,
    JobService jobs,
    AuditService audit,
    ILogger<JobDiscoveryService> logger)
{
    private static readonly string[] DefaultCountries =
    [
        "United Kingdom", "Germany", "Netherlands", "Ireland", "France", "Belgium", "Luxembourg", "Switzerland",
        "Sweden", "Norway", "Denmark", "Finland", "Austria", "Spain", "Portugal", "Italy", "Poland",
        "Czech Republic", "Hungary", "Romania"
    ];

    private static readonly Dictionary<string, string[]> CountryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United Kingdom"] = ["united kingdom", "uk", "england", "scotland", "wales", "northern ireland", "london", "manchester", "birmingham", "bristol", "leeds"],
        ["Germany"] = ["germany", "berlin", "munich", "hamburg", "frankfurt"],
        ["Netherlands"] = ["netherlands", "holland", "amsterdam", "rotterdam", "utrecht", "eindhoven"],
        ["Ireland"] = ["ireland", "dublin", "cork", "galway"],
        ["France"] = ["france", "paris", "lyon", "toulouse"],
        ["Belgium"] = ["belgium", "brussels", "antwerp", "ghent"],
        ["Luxembourg"] = ["luxembourg"],
        ["Switzerland"] = ["switzerland", "zurich", "geneva", "basel", "bern"],
        ["Sweden"] = ["sweden", "stockholm", "gothenburg", "malmo"],
        ["Norway"] = ["norway", "oslo", "bergen", "trondheim"],
        ["Denmark"] = ["denmark", "copenhagen", "aarhus"],
        ["Finland"] = ["finland", "helsinki", "espoo", "tampere"],
        ["Austria"] = ["austria", "vienna", "linz", "graz"],
        ["Spain"] = ["spain", "madrid", "barcelona", "valencia"],
        ["Portugal"] = ["portugal", "lisbon", "porto", "braga"],
        ["Italy"] = ["italy", "milan", "rome", "turin"],
        ["Poland"] = ["poland", "warsaw", "krakow", "wroclaw", "gdansk"],
        ["Czech Republic"] = ["czech republic", "czechia", "prague", "brno"],
        ["Hungary"] = ["hungary", "budapest"],
        ["Romania"] = ["romania", "bucharest", "cluj", "timisoara"],
        ["Other European Countries"] = ["europe", "european union", "eu", "eea"]
    };

    private static readonly string[] SponsorshipKeywords =
    [
        "visa sponsorship", "visa-sponsored", "sponsorship available", "sponsorship provided", "will sponsor", "can sponsor",
        "skilled worker visa", "work permit sponsorship", "relocation support", "relocation package", "immigration support"
    ];

    private static readonly string[] SponsorshipExclusionKeywords =
    [
        "no visa sponsorship", "cannot sponsor", "unable to sponsor", "not offer sponsorship", "without sponsorship"
    ];

    public async Task<JobDiscoveryResult> DiscoverAsync(Guid userId, int maxResults, CancellationToken cancellationToken)
    {
        var profile = await profiles.GetCurrentAsync(cancellationToken);
        var verifiedFacts = await facts.ListVerifiedAsync(cancellationToken);
        var jobSearchPreferences = await preferences.GetAsync(cancellationToken);
        var request = BuildRequest(profile, verifiedFacts, jobSearchPreferences, maxResults);
        var notes = new List<string>();

        if (!sources.Any())
        {
            notes.Add("No public career sources are configured in this build.");
            return new JobDiscoveryResult([], 0, 0, 0, [], notes);
        }

        var fetched = new List<DiscoveredJobPosting>();
        foreach (var source in sources)
        {
            try
            {
                fetched.AddRange(await source.SearchAsync(request, cancellationToken));
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Job discovery source {Source} failed", source.Name);
                notes.Add($"Source {source.Name} failed: {ex.Message}");
            }
        }

        var ranked = fetched
            .Where(posting => MatchesTargetCountries(posting, request.TargetRegions))
            .Where(posting => !request.RequireVisaSponsorship || HasVisaSponsorship(posting))
            .Select(posting => Rank(posting, request))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Posting.Company, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxResults)
            .ToList();

        var existing = await jobs.ListAsync(cancellationToken);
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var persisted = new List<Job>();

        foreach (var rankedPosting in ranked)
        {
            var posting = rankedPosting.Posting;
            var duplicate = existing.FirstOrDefault(j => SamePosting(j, posting));
            if (duplicate is null)
            {
                var created = await jobs.CreateAsync(posting.Title, posting.Company, posting.Location, posting.Source, posting.Url, posting.Description, userId, cancellationToken);
                persisted.Add(created);
                imported++;
                existing = [.. existing, created];
                continue;
            }

            if (NeedsRefresh(duplicate, posting))
            {
                var refreshed = duplicate with
                {
                    Title = posting.Title,
                    Company = posting.Company,
                    Location = posting.Location,
                    Source = posting.Source,
                    Url = posting.Url,
                    Description = posting.Description,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await jobs.SaveAsync(refreshed, cancellationToken);
                persisted.Add(refreshed);
                updated++;
            }
            else
            {
                persisted.Add(duplicate);
                skipped++;
            }
        }

        await audit.RecordAsync(userId, "JOB_DISCOVERY_COMPLETED", "Job", "search", "SUCCESS",
            $"Fetched={fetched.Count}; Ranked={ranked.Count}; Imported={imported}; Updated={updated}; Skipped={skipped}", cancellationToken);
        logger.LogInformation("Job discovery completed: fetched={Fetched}, ranked={Ranked}, imported={Imported}, updated={Updated}, skipped={Skipped}",
            fetched.Count, ranked.Count, imported, updated, skipped);

        if (ranked.Count == 0)
        {
            notes.Add($"No jobs were found for the selected countries ({string.Join(", ", request.TargetRegions)}) with the current skill, role, and sponsorship filters.");
        }

        return new JobDiscoveryResult(
            persisted.OrderByDescending(j => j.CreatedAt).ToList(),
            imported,
            updated,
            skipped,
            sources.Select(s => s.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            notes);
    }

    private static JobDiscoveryRequest BuildRequest(CandidateProfile? profile, IReadOnlyList<CandidateFact> facts, JobSearchPreferences preferences, int maxResults)
    {
        var skills = facts.Where(f => f.FactType == "SKILL").Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var factPreferences = facts.Where(f => f.FactType == "PREFERENCE").Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var preferredRoles = preferences.PreferredRoles.Count > 0 ? preferences.PreferredRoles : profile is null || string.IsNullOrWhiteSpace(profile.CurrentRole) ? [] : [profile.CurrentRole];
        var countries = preferences.PreferredCountries.Count > 0 ? preferences.PreferredCountries : DefaultCountries;
        var mergedPreferences = factPreferences
            .Concat(preferredRoles)
            .Concat(string.IsNullOrWhiteSpace(preferences.WorkAuthorization) ? [] : [preferences.WorkAuthorization])
            .Concat(string.IsNullOrWhiteSpace(preferences.WorkMode) ? [] : [preferences.WorkMode])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new JobDiscoveryRequest(
            profile?.CurrentRole,
            skills,
            mergedPreferences,
            preferredRoles,
            countries,
            preferences.WorkMode,
            preferences.EffectiveLinkedInProfileUrl,
            RequireVisaSponsorship: true,
            Math.Clamp(maxResults, 1, 50));
    }

    private static RankedDiscoveredJob Rank(DiscoveredJobPosting posting, JobDiscoveryRequest request)
    {
        var haystack = $"{posting.Title}\n{posting.Description}\n{posting.Tags.Count switch { > 0 => string.Join(' ', posting.Tags), _ => string.Empty }}";
        var matchedSkills = request.Skills.Where(skill => ContainsPhrase(haystack, skill)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var roleScore = Math.Max(RoleScore(posting.Title, request.CurrentRole), PreferredRoleScore(posting.Title, request.PreferredRoles));
        var locationScore = CountryScore(posting, request.TargetRegions);
        var workModeScore = WorkModeScore(posting, request.WorkMode);
        var score = Math.Min(100, matchedSkills.Count * 12 + roleScore + PreferenceScore(posting, request.Preferences) + SponsorshipScore(posting) + locationScore + workModeScore);
        var reasons = new List<string>();
        if (matchedSkills.Count > 0) reasons.Add($"Matched skills: {string.Join(", ", matchedSkills.Take(6))}");
        if (roleScore > 0) reasons.Add($"Role alignment with {string.Join(" / ", request.PreferredRoles.DefaultIfEmpty(request.CurrentRole ?? "current role"))}");
        if (locationScore > 0) reasons.Add($"Country matches {string.Join(", ", request.TargetRegions.Take(3))}{(request.TargetRegions.Count > 3 ? "..." : string.Empty)}");
        if (workModeScore > 0 && !string.IsNullOrWhiteSpace(request.WorkMode)) reasons.Add($"Matches preferred work mode ({request.WorkMode})");
        if (request.RequireVisaSponsorship) reasons.Add("Posting mentions visa sponsorship or relocation support.");
        return new RankedDiscoveredJob(posting, score, matchedSkills, reasons);
    }

    private static int RoleScore(string title, string? currentRole)
    {
        if (string.IsNullOrWhiteSpace(currentRole)) return 0;
        var titleTokens = Tokenize(title);
        var roleTokens = Tokenize(currentRole);
        var overlap = roleTokens.Count == 0 ? 0 : roleTokens.Count(token => titleTokens.Contains(token));
        return overlap switch
        {
            >= 3 => 25,
            2 => 18,
            1 => 10,
            _ => 0
        };
    }

    private static int PreferredRoleScore(string title, IReadOnlyList<string> preferredRoles)
    {
        var scores = preferredRoles.Select(role => RoleScore(title, role)).ToList();
        return scores.Count == 0 ? 0 : scores.Max();
    }

    private static int PreferenceScore(DiscoveredJobPosting posting, IReadOnlyList<string> preferences)
    {
        if (preferences.Count == 0) return 0;
        var haystack = $"{posting.Location}\n{posting.Description}";
        return Math.Min(15, preferences.Count(preference => ContainsPhrase(haystack, preference)) * 5);
    }

    private static int CountryScore(DiscoveredJobPosting posting, IReadOnlyList<string> countries) =>
        MatchesTargetCountries(posting, countries) ? 10 : 0;

    private static int WorkModeScore(DiscoveredJobPosting posting, string? workMode)
    {
        if (string.IsNullOrWhiteSpace(workMode)) return 0;
        var haystack = $"{posting.Title}\n{posting.Description}\n{posting.Location}";
        return ContainsPhrase(haystack, workMode) ? 8 : 0;
    }

    private static int SponsorshipScore(DiscoveredJobPosting posting)
    {
        var haystack = $"{posting.Title}\n{posting.Description}\n{posting.Location}";
        return SponsorshipKeywords.Count(keyword => ContainsPhrase(haystack, keyword)) > 1 ? 10 : 5;
    }

    private static bool MatchesTargetCountries(DiscoveredJobPosting posting, IReadOnlyList<string> countries)
    {
        if (countries.Count == 0) return true;
        var haystack = $"{posting.Location}\n{posting.Description}";
        return countries.Any(country => CountryKeywordsFor(country).Any(keyword => ContainsPhrase(haystack, keyword)));
    }

    private static IEnumerable<string> CountryKeywordsFor(string country) =>
        CountryKeywords.TryGetValue(country, out var values) ? values : [country];

    private static bool HasVisaSponsorship(DiscoveredJobPosting posting)
    {
        var haystack = $"{posting.Title}\n{posting.Description}\n{posting.Location}";
        return !SponsorshipExclusionKeywords.Any(keyword => ContainsPhrase(haystack, keyword))
            && SponsorshipKeywords.Any(keyword => ContainsPhrase(haystack, keyword));
    }

    private static bool SamePosting(Job job, DiscoveredJobPosting posting)
    {
        if (!string.IsNullOrWhiteSpace(job.Url) && !string.IsNullOrWhiteSpace(posting.Url))
        {
            return string.Equals(job.Url, posting.Url, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(job.Title, posting.Title, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Company, posting.Company, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Location, posting.Location, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NeedsRefresh(Job job, DiscoveredJobPosting posting) =>
        !string.Equals(job.Description, posting.Description, StringComparison.Ordinal)
        || !string.Equals(job.Location, posting.Location, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(job.Source, posting.Source, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(job.Url, posting.Url, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsPhrase(string text, string phrase) =>
        text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> Tokenize(string value) =>
        Regex.Split(value.ToLowerInvariant(), "[^a-z0-9+#.]+")
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
