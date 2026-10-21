using System.Text.RegularExpressions;

namespace ArvindJobHunter.Worker.Browser;

/// <summary>A form control found on an application page.</summary>
public sealed record PageField(int Index, string Label, string Kind, bool Required, IReadOnlyList<string> Options);

/// <summary>
/// Maps form labels to the user's answers. Pure logic so it can be tested without a browser.
/// Answers come from the API (verified profile facts plus anything the user typed); nothing is guessed.
/// </summary>
public static partial class FieldMatcher
{
    private static readonly (string Key, Regex Pattern)[] Aliases =
    [
        ("First name", Rx("first\\s*name|given\\s*name|forename")),
        ("Last name", Rx("last\\s*name|surname|family\\s*name")),
        ("Full name", Rx("^\\s*(full\\s*)?name\\s*\\*?$|your\\s*name")),
        ("Email", Rx("e-?mail")),
        ("Phone", Rx("phone|mobile|telephone")),
        ("LinkedIn", Rx("linkedin")),
        ("Website", Rx("website|portfolio|github")),
        ("Location", Rx("^\\s*(current\\s*)?(location|city)")),
        ("Cover letter", Rx("cover\\s*letter|motivation")),
        ("Notice period", Rx("notice\\s*period|start\\s*date|available\\s*to\\s*start")),
        ("Salary expectation", Rx("salary|compensation|pay\\s*expectation")),
        ("Work authorization", Rx("authori[sz]ed|right\\s*to\\s*work|work\\s*permit")),
        ("Sponsorship", Rx("sponsor")),
        ("Years of experience", Rx("years\\s*of\\s*experience"))
    ];

    public static bool IsResumeUpload(PageField field) =>
        field.Kind == "file" && !Rx("cover").IsMatch(field.Label);

    public static bool IsCoverLetterUpload(PageField field) =>
        field.Kind == "file" && Rx("cover").IsMatch(field.Label);

    /// <summary>Returns the canonical key for a label (e.g. "Given name *" -> "First name"), or the cleaned label.</summary>
    public static string Canonical(string label)
    {
        var clean = Clean(label);
        foreach (var (key, pattern) in Aliases)
            if (pattern.IsMatch(clean)) return key;
        return clean;
    }

    public static string? Answer(PageField field, IReadOnlyDictionary<string, string> answers)
    {
        var clean = Clean(field.Label);
        if (answers.TryGetValue(clean, out var exact)) return exact;
        var key = Canonical(field.Label);
        if (answers.TryGetValue(key, out var value)) return value;
        if (key == "Full name" && answers.TryGetValue("First name", out var first) && answers.TryGetValue("Last name", out var last))
            return $"{first} {last}";
        return null;
    }

    /// <summary>Picks the option matching an answer (case-insensitive, then prefix), so selects never get free text.</summary>
    public static string? PickOption(IReadOnlyList<string> options, string answer) =>
        options.FirstOrDefault(o => string.Equals(o.Trim(), answer.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? options.FirstOrDefault(o => o.Trim().StartsWith(answer.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Clean(string label) => Whitespace().Replace(label.Replace("*", ""), " ").Trim();

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
