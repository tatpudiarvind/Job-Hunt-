namespace ArvindJobHunter.Domain.Entities;

public enum SpellingVariant { US, UK }

/// <summary>Resume conventions for one hiring country.</summary>
public sealed record CountryProfile(
    string Code,
    string Name,
    string DocumentName,
    int MaxPages,
    bool PhotoExpected,
    bool IncludeDateOfBirth,
    bool IncludeNationality,
    SpellingVariant Spelling,
    string DateFormat,
    IReadOnlyList<string> RequiredSections,
    IReadOnlyList<string> Notes);

/// <summary>Built-in country profiles and location-text detection.</summary>
public static class CountryProfiles
{
    private static readonly string[] CoreSections = ["Summary", "Experience", "Skills", "Education"];

    public static readonly CountryProfile Default = new("XX", "Other", "Resume", 2, false, false, false, SpellingVariant.US, "MMM yyyy", CoreSections,
        ["No country-specific rules; using a neutral, ATS-friendly format."]);

    public static readonly IReadOnlyList<CountryProfile> All =
    [
        new("US", "United States", "Resume", 2, false, false, false, SpellingVariant.US, "MMM yyyy", CoreSections,
            ["No photo, date of birth, marital status or nationality (anti-discrimination norms).", "1 page under 10 years of experience, otherwise 2."]),
        new("CA", "Canada", "Resume", 2, false, false, false, SpellingVariant.US, "MMM yyyy", CoreSections,
            ["No photo or personal details.", "Mention work permit status if not a citizen or PR."]),
        new("GB", "United Kingdom", "CV", 2, false, false, false, SpellingVariant.UK, "MMM yyyy", CoreSections,
            ["No photo or date of birth.", "State right to work or sponsorship need.", "Use UK spelling."]),
        new("IE", "Ireland", "CV", 2, false, false, false, SpellingVariant.UK, "MMM yyyy", CoreSections,
            ["No photo.", "State Stamp 4 / work permit status."]),
        new("DE", "Germany", "Lebenslauf", 2, true, true, true, SpellingVariant.UK, "MM/yyyy", [.. CoreSections, "Languages"],
            ["Professional photo is common.", "Include date of birth, nationality and language levels (CEFR).", "Reverse-chronological, tabular style is expected."]),
        new("NL", "Netherlands", "CV", 2, false, false, true, SpellingVariant.UK, "MM/yyyy", [.. CoreSections, "Languages"],
            ["Photo optional.", "Mention nationality and whether you need a highly-skilled-migrant permit."]),
        new("AU", "Australia", "Resume", 3, false, false, false, SpellingVariant.UK, "MMM yyyy", [.. CoreSections, "Referees"],
            ["2-3 pages is normal.", "Add referees or 'Referees available on request'.", "State visa status."]),
        new("NZ", "New Zealand", "CV", 3, false, false, false, SpellingVariant.UK, "MMM yyyy", [.. CoreSections, "Referees"],
            ["Include referees.", "State visa status."]),
        new("AE", "United Arab Emirates", "CV", 2, true, true, true, SpellingVariant.UK, "MMM yyyy", CoreSections,
            ["Photo, nationality and visa status are commonly included.", "Mention notice period and availability."]),
        new("SG", "Singapore", "Resume", 2, false, false, true, SpellingVariant.UK, "MMM yyyy", CoreSections,
            ["Mention nationality and Employment Pass eligibility.", "Expected salary is often requested."]),
        new("IN", "India", "Resume", 2, false, true, true, SpellingVariant.UK, "MMM yyyy", [.. CoreSections, "Personal Details"],
            ["A short personal-details section is common.", "Mention notice period and current/expected CTC when asked."]),
    ];

    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = ["United States", "USA", "U.S.", "US-", "Remote US", "New York", "San Francisco", "Seattle", "Austin", "Boston", "Chicago", "California", "Texas"],
        ["CA"] = ["Canada", "Toronto", "Vancouver", "Montreal", "Ottawa", "Calgary"],
        ["GB"] = ["United Kingdom", "UK", "England", "Scotland", "Wales", "London", "Manchester", "Edinburgh", "Birmingham", "Cambridge"],
        ["IE"] = ["Ireland", "Dublin", "Cork", "Galway"],
        ["DE"] = ["Germany", "Deutschland", "Berlin", "Munich", "München", "Hamburg", "Frankfurt", "Cologne", "Stuttgart"],
        ["NL"] = ["Netherlands", "Amsterdam", "Rotterdam", "Utrecht", "Eindhoven", "The Hague"],
        ["AU"] = ["Australia", "Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide"],
        ["NZ"] = ["New Zealand", "Auckland", "Wellington", "Christchurch"],
        ["AE"] = ["United Arab Emirates", "UAE", "Dubai", "Abu Dhabi", "Sharjah"],
        ["SG"] = ["Singapore"],
        ["IN"] = ["India", "Bengaluru", "Bangalore", "Hyderabad", "Pune", "Chennai", "Mumbai", "Gurugram", "Gurgaon", "Noida", "Delhi"],
    };

    public const int PreferredCountryBonus = 5;

    /// <summary>Raises the score of a job in one of the user's preferred countries (names, aliases or ISO codes).</summary>
    public static JobMatch BoostForPreferredCountry(JobMatch match, string? jobCountryCode, IEnumerable<string> preferredCountries)
    {
        if (jobCountryCode is null) return match;
        var preferred = preferredCountries.Select(Normalize).Where(c => c is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!preferred.Contains(jobCountryCode)) return match;
        return match with
        {
            Score = Math.Min(100, match.Score + PreferredCountryBonus),
            Reason = $"{match.Reason} (+{PreferredCountryBonus}: {Get(jobCountryCode).Name} is a preferred country)".Trim()
        };
    }

    public static CountryProfile Get(string? code) =>
        All.FirstOrDefault(p => string.Equals(p.Code, Normalize(code), StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>Maps common aliases (UK, England, Deutschland...) or a country name to an ISO code.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Equals("UK", StringComparison.OrdinalIgnoreCase)) return "GB";
        var byCode = All.FirstOrDefault(p => p.Code.Equals(v, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null) return byCode.Code;
        var byName = All.FirstOrDefault(p => p.Name.Equals(v, StringComparison.OrdinalIgnoreCase));
        return byName?.Code ?? Detect(v);
    }

    /// <summary>Detects the country from free location text such as "London, UK (Hybrid)". Returns null when unknown.</summary>
    public static string? Detect(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        foreach (var (code, words) in Keywords)
        {
            foreach (var word in words)
            {
                if (ContainsWord(location, word)) return code;
            }
        }
        return null;
    }

    private static bool ContainsWord(string text, string word)
    {
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetter(text[index - 1]);
            var end = index + word.Length;
            var after = end >= text.Length || !char.IsLetter(text[end]);
            // Short all-caps codes (UK, USA, UAE) must match case-sensitively to avoid hits inside words.
            var caseOk = word.Length > 3 || string.CompareOrdinal(text, index, word, 0, word.Length) == 0;
            if (before && after && caseOk) return true;
            index = end;
        }
        return false;
    }
}
