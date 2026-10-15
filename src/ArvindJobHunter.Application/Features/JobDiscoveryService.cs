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
    public const int MinimumSkillMatchPercent = 60;
    private const int MinimumRequiredSkillsDetected = 3;

    private static readonly string[] DefaultCountries =
    [
        "United Kingdom", "Germany", "Netherlands", "Ireland", "France", "Belgium", "Luxembourg", "Switzerland",
        "Sweden", "Norway", "Denmark", "Finland", "Iceland", "Austria", "Spain", "Portugal", "Italy", "Malta", "Greece", "Cyprus",
        "Poland", "Czech Republic", "Slovakia", "Hungary", "Romania", "Bulgaria", "Croatia", "Slovenia", "Serbia",
        "Estonia", "Latvia", "Lithuania", "Other European Countries"
    ];

    private static readonly Dictionary<string, string[]> CountryKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["United Kingdom"] = ["united kingdom", "uk", "england", "scotland", "wales", "northern ireland", "london", "manchester", "birmingham", "bristol", "leeds", "edinburgh", "glasgow", "cambridge", "oxford"],
        ["Germany"] = ["germany", "deutschland", "berlin", "munich", "münchen", "hamburg", "frankfurt", "cologne", "köln", "stuttgart", "düsseldorf", "dusseldorf", "leipzig", "dresden", "karlsruhe", "nuremberg"],
        ["Netherlands"] = ["netherlands", "holland", "amsterdam", "rotterdam", "utrecht", "eindhoven", "the hague", "den haag"],
        ["Ireland"] = ["ireland", "dublin", "cork", "galway", "limerick"],
        ["France"] = ["france", "paris", "lyon", "toulouse", "nantes", "lille", "bordeaux", "marseille"],
        ["Belgium"] = ["belgium", "brussels", "antwerp", "ghent", "leuven"],
        ["Luxembourg"] = ["luxembourg"],
        ["Switzerland"] = ["switzerland", "schweiz", "zurich", "zürich", "geneva", "basel", "bern", "lausanne"],
        ["Sweden"] = ["sweden", "stockholm", "gothenburg", "malmo", "malmö"],
        ["Norway"] = ["norway", "oslo", "bergen", "trondheim"],
        ["Denmark"] = ["denmark", "copenhagen", "aarhus"],
        ["Finland"] = ["finland", "helsinki", "espoo", "tampere"],
        ["Iceland"] = ["iceland", "reykjavik"],
        ["Austria"] = ["austria", "vienna", "wien", "linz", "graz"],
        ["Spain"] = ["spain", "madrid", "barcelona", "valencia", "malaga", "málaga", "seville"],
        ["Portugal"] = ["portugal", "lisbon", "lisboa", "porto", "braga"],
        ["Italy"] = ["italy", "milan", "milano", "rome", "turin", "bologna"],
        ["Malta"] = ["malta", "valletta"],
        ["Greece"] = ["greece", "athens", "thessaloniki"],
        ["Cyprus"] = ["cyprus", "limassol", "nicosia"],
        ["Poland"] = ["poland", "warsaw", "krakow", "kraków", "wroclaw", "wrocław", "gdansk", "poznan"],
        ["Czech Republic"] = ["czech republic", "czechia", "prague", "brno"],
        ["Slovakia"] = ["slovakia", "bratislava", "kosice"],
        ["Hungary"] = ["hungary", "budapest"],
        ["Romania"] = ["romania", "bucharest", "cluj", "timisoara", "iasi"],
        ["Bulgaria"] = ["bulgaria", "sofia", "plovdiv"],
        ["Croatia"] = ["croatia", "zagreb", "split"],
        ["Slovenia"] = ["slovenia", "ljubljana"],
        ["Serbia"] = ["serbia", "belgrade", "novi sad"],
        ["Estonia"] = ["estonia", "tallinn", "tartu"],
        ["Latvia"] = ["latvia", "riga"],
        ["Lithuania"] = ["lithuania", "vilnius", "kaunas"],
        ["Other European Countries"] = ["europe", "european union", "emea", "eea", "remote - europe", "remote europe"]
    };

    // Technical terms used to detect what a posting actually requires, so skill coverage can be measured.
    private static readonly string[] TechVocabulary =
    [
        "C#", ".NET", ".NET Core", "ASP.NET", "ASP.NET Core", "Entity Framework", "Blazor", "WPF", "Java", "Spring Boot", "Kotlin", "Scala",
        "Python", "Django", "Flask", "FastAPI", "Golang", "Rust", "C++", "Ruby", "Ruby on Rails", "PHP", "Laravel", "Elixir",
        "JavaScript", "TypeScript", "Node.js", "React", "Angular", "Vue", "Next.js", "SwiftUI", "Objective-C", "Android", "iOS", "Flutter",
        "SQL", "SQL Server", "PostgreSQL", "MySQL", "MongoDB", "Redis", "Elasticsearch", "Cassandra", "DynamoDB", "Snowflake",
        "Kafka", "RabbitMQ", "Azure", "AWS", "GCP", "Docker", "Kubernetes", "Terraform", "Microservices", "REST API", "RESTful", "gRPC", "GraphQL",
        "CI/CD", "Machine Learning", "LLM", "RAG", "OpenAI", "Spark", "Hadoop", "Airflow", "dbt", "Linux"
    ];

    private static readonly string[] SponsorshipKeywords =
    [
        "visa sponsorship", "visa-sponsored", "sponsorship available", "sponsorship provided", "will sponsor", "can sponsor",
        "skilled worker visa", "work permit sponsorship", "blue card", "eu blue card", "visa support", "visa assistance", "relocation support", "relocation package", "relocation assistance", "immigration support"
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

        var rankedAll = fetched
            .Where(posting => MatchesTargetCountries(posting, request.TargetRegions))
            .Where(posting => !request.RequireVisaSponsorship || HasVisaSponsorship(posting))
            .Select(posting => Rank(posting, request))
            .ToList();
        var ranked = rankedAll
            .Where(r => r.Score > 0 && SkillCoveragePercent(r.Posting, request.Skills) >= MinimumSkillMatchPercent)
            .OrderByDescending(r => SkillCoveragePercent(r.Posting, request.Skills))
            .ThenByDescending(r => r.Score)
            .ThenBy(r => r.Posting.Company, StringComparer.OrdinalIgnoreCase)
            .Take(request.MaxResults)
            .ToList();
        if (rankedAll.Count > ranked.Count)
        {
            notes.Add($"{rankedAll.Count - ranked.Count} sponsored European posting(s) were skipped because less than {MinimumSkillMatchPercent}% of their required skills match your resume.");
        }

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
                created = created with { Sponsorship = EvaluateSponsorship(posting.Title, posting.Description, posting.Location, posting.Source) };
                await jobs.SaveAsync(created, cancellationToken);
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
                    Sponsorship = EvaluateSponsorship(posting.Title, posting.Description, posting.Location, posting.Source),
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
        reasons.Add($"Skill match: {SkillCoveragePercent(posting, request.Skills)}% of the posting's required skills are on your resume");
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

    private static bool HasVisaSponsorship(DiscoveredJobPosting posting) =>
        EvaluateSponsorship(posting.Title, posting.Description, posting.Location, posting.Source).Status == SponsorshipStatus.MENTIONED;

    public static SponsorshipEvidence EvaluateSponsorship(string title, string description, string location, string source)
    {
        var haystack = $"{title}\n{description}\n{location}";
        var now = DateTimeOffset.UtcNow;
        var exclusion = SponsorshipExclusionKeywords.FirstOrDefault(keyword => ContainsPhrase(haystack, keyword));
        if (exclusion is not null) return new SponsorshipEvidence(SponsorshipStatus.EXCLUDED, exclusion, source, now);
        var mention = SponsorshipKeywords.FirstOrDefault(keyword => ContainsPhrase(haystack, keyword));
        return new SponsorshipEvidence(mention is null ? SponsorshipStatus.UNKNOWN : SponsorshipStatus.MENTIONED, mention, source, now);
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
        !string.IsNullOrWhiteSpace(phrase)
        && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(phrase.Trim())}(?![\p{{L}}\p{{N}}+#])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Share (0-100) of the technical skills a posting asks for that also appear among the candidate's verified resume skills.</summary>
    public static int SkillCoveragePercent(DiscoveredJobPosting posting, IReadOnlyList<string> candidateSkills)
    {
        if (candidateSkills.Count == 0) return 0;
        var haystack = $"{posting.Title}\n{posting.Description}\n{string.Join(' ', posting.Tags)}";
        var required = TechVocabulary
            .Concat(candidateSkills)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(term => ContainsPhrase(haystack, term))
            .ToList();
        // Collapse terms that are part of a longer detected term (".NET" inside "ASP.NET Core") so they are not double counted.
        required = required
            .Where(term => !required.Any(other => other.Length > term.Length && other.Contains(term, StringComparison.OrdinalIgnoreCase) && !candidateSkills.Contains(term, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (required.Count < MinimumRequiredSkillsDetected) return 0;
        var matched = required.Count(term => candidateSkills.Any(skill =>
            string.Equals(skill, term, StringComparison.OrdinalIgnoreCase)
            || skill.Contains(term, StringComparison.OrdinalIgnoreCase)
            || term.Contains(skill, StringComparison.OrdinalIgnoreCase)));
        return (int)Math.Round(matched * 100.0 / required.Count);
    }

    private static HashSet<string> Tokenize(string value) =>
        Regex.Split(value.ToLowerInvariant(), "[^a-z0-9+#.]+")
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
