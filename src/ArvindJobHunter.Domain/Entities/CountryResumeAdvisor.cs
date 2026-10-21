using System.Text.RegularExpressions;

namespace ArvindJobHunter.Domain.Entities;

/// <summary>Checks resume text against a country's conventions and converts spelling. Pure and deterministic.</summary>
public static partial class CountryResumeAdvisor
{
    private const int CharactersPerPage = 2800;

    private static readonly (string Us, string Uk)[] SpellingPairs =
    [
        ("optimize", "optimise"), ("optimized", "optimised"), ("optimizing", "optimising"), ("optimization", "optimisation"),
        ("organize", "organise"), ("organized", "organised"), ("organization", "organisation"), ("organizations", "organisations"),
        ("analyze", "analyse"), ("analyzed", "analysed"), ("analyzing", "analysing"),
        ("prioritize", "prioritise"), ("prioritized", "prioritised"), ("standardize", "standardise"), ("standardized", "standardised"),
        ("modernize", "modernise"), ("modernized", "modernised"), ("utilize", "utilise"), ("utilized", "utilised"),
        ("customize", "customise"), ("customized", "customised"), ("centralize", "centralise"), ("centralized", "centralised"),
        ("containerize", "containerise"), ("containerized", "containerised"), ("recognized", "recognised"),
        ("behavior", "behaviour"), ("color", "colour"), ("favorite", "favourite"), ("labor", "labour"),
        ("center", "centre"), ("license", "licence"), ("catalog", "catalogue"), ("program manager", "programme manager"),
    ];

    /// <summary>Returns human-readable issues; empty means the resume fits the country's conventions.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<string> paragraphs, CountryProfile profile)
    {
        var issues = new List<string>();
        var text = string.Join("\n", paragraphs);
        foreach (var section in profile.RequiredSections)
        {
            if (!paragraphs.Any(p => IsHeadingFor(p, section)))
                issues.Add($"{profile.Name}: add a \"{section}\" section.");
        }

        var pages = Math.Max(1, (int)Math.Ceiling(text.Length / (double)CharactersPerPage));
        if (pages > profile.MaxPages)
            issues.Add($"{profile.Name}: about {pages} pages; aim for at most {profile.MaxPages}.");

        if (!profile.IncludeDateOfBirth && DateOfBirth().IsMatch(text))
            issues.Add($"{profile.Name}: remove the date of birth.");
        if (profile.Spelling == SpellingVariant.UK && FindVariant(text, SpellingVariant.US).Count > 0)
            issues.Add($"{profile.Name}: use UK spelling ({string.Join(", ", FindVariant(text, SpellingVariant.US).Take(4))}).");
        if (profile.Spelling == SpellingVariant.US && FindVariant(text, SpellingVariant.UK).Count > 0)
            issues.Add($"{profile.Name}: use US spelling ({string.Join(", ", FindVariant(text, SpellingVariant.UK).Take(4))}).");
        return issues;
    }

    /// <summary>Converts words to the target spelling, keeping the original capitalisation.</summary>
    public static string ToSpelling(string text, SpellingVariant target)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (us, uk) in SpellingPairs)
        {
            var (from, to) = target == SpellingVariant.UK ? (us, uk) : (uk, us);
            text = Regex.Replace(text, $@"\b{Regex.Escape(from)}\b", m => MatchCase(m.Value, to), RegexOptions.IgnoreCase);
        }
        return text;
    }

    public static ResumeChange ToSpelling(ResumeChange change, SpellingVariant target) =>
        change with { NewText = ToSpelling(change.NewText, target) };

    private static List<string> FindVariant(string text, SpellingVariant variant) =>
        SpellingPairs.Select(p => variant == SpellingVariant.US ? p.Us : p.Uk)
            .Where(w => Regex.IsMatch(text, $@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase))
            .ToList();

    private static bool IsHeadingFor(string paragraph, string section)
    {
        var p = paragraph.Trim().TrimEnd(':');
        if (p.Length == 0 || p.Length > 40) return false;
        return section switch
        {
            "Summary" => Regex.IsMatch(p, @"\b(summary|profile|about me|objective)\b", RegexOptions.IgnoreCase),
            "Experience" => Regex.IsMatch(p, @"\b(experience|employment|work history|career)\b", RegexOptions.IgnoreCase),
            "Skills" => Regex.IsMatch(p, @"\b(skills|competencies|technologies|tech stack|expertise)\b", RegexOptions.IgnoreCase),
            "Education" => Regex.IsMatch(p, @"\b(education|qualifications|academic)\b", RegexOptions.IgnoreCase),
            "Languages" => Regex.IsMatch(p, @"\blanguages?\b", RegexOptions.IgnoreCase),
            "Referees" => Regex.IsMatch(p, @"\b(referees|references)\b", RegexOptions.IgnoreCase),
            "Personal Details" => Regex.IsMatch(p, @"\b(personal (details|information)|personal data)\b", RegexOptions.IgnoreCase),
            _ => p.Contains(section, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static string MatchCase(string source, string replacement) =>
        source.All(char.IsUpper) ? replacement.ToUpperInvariant()
        : char.IsUpper(source[0]) ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
        : replacement;

    [GeneratedRegex(@"\b(date of birth|DOB|born on)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateOfBirth();
}
