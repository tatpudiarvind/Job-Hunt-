using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Agents.Llm;

/// <summary>
/// Deterministic, offline provider. Produces structured JSON for known schema names by inspecting the
/// prompt content with simple heuristics. Never contacts a network.
/// </summary>
public sealed partial class DemoLlmProvider : ILlmProvider
{
    private static readonly string[] KnownSkills =
    [
        "C#", ".NET", "ASP.NET Core", "Microservices", "Azure", "AWS", "RAG", "LLMs", "LLM", "OpenAI", "Healthcare", "DICOM", "Medical Imaging",
        "Angular", "React", "TypeScript", "WPF", "NUnit", "xUnit", "Integration Testing", "SQL", "SQL Server", "PostgreSQL", "Docker", "Kubernetes",
        "REST", "gRPC", "Entity Framework", "CI/CD", "Git", "Python", "Java", "Agile", "Scrum", "DevOps", "Terraform", "Kafka", "RabbitMQ", "Redis"
    ];

    public string Name => "Demo";
    public bool IsConfigured => true;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var user = string.Join("\n", request.Messages.Where(m => m.Role == "user").Select(m => m.Content));
        var content = request.JsonSchemaName switch
        {
            "JobAnalysis" => AnalyzeJob(user),
            "JobMatch" => MatchJob(user),
            "ResumeChanges" => ProposeResumeChanges(user),
            "CoverLetter" => CoverLetter(user),
            "RecruiterEmail" => RecruiterEmail(user),
            _ => JsonSerializer.Serialize(new { text = "Demo provider response.", echo = user.Length })
        };

        return Task.FromResult(new LlmResponse(content, Name, "demo-v1", user.Length / 4, content.Length / 4));
    }

    private static string AnalyzeJob(string description)
    {
        var found = KnownSkills.Where(s => description.Contains(s, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var required = found.Take(Math.Max(1, found.Count * 2 / 3)).ToList();
        var nice = found.Skip(required.Count).ToList();
        var seniority = SeniorityRegex().Match(description) is { Success: true } m ? Capitalize(m.Value) : "Mid-level";
        var responsibilities = description.Split(['\n', '.'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length is > 20 and < 200).Take(5).ToList();
        return JsonSerializer.Serialize(new
        {
            requiredSkills = required,
            niceToHaveSkills = nice,
            seniorityLevel = seniority,
            summary = $"{seniority} role requiring {string.Join(", ", required.Take(4))}.",
            responsibilities
        });
    }

    private static string MatchJob(string input)
    {
        var sections = ParseSections(input);
        var required = SplitList(sections.GetValueOrDefault("REQUIRED_SKILLS", ""));
        var nice = SplitList(sections.GetValueOrDefault("NICE_TO_HAVE_SKILLS", ""));
        var facts = SplitList(sections.GetValueOrDefault("VERIFIED_SKILLS", ""));
        var matched = required.Concat(nice).Where(s => facts.Any(f => Equivalent(f, s))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missing = required.Where(s => !facts.Any(f => Equivalent(f, s))).ToList();
        var score = required.Count == 0 ? (facts.Count > 0 ? 50 : 0)
            : (int)Math.Round(100.0 * required.Count(s => facts.Any(f => Equivalent(f, s))) / required.Count * 0.8 + (nice.Count == 0 ? 20 : 20.0 * nice.Count(s => facts.Any(f => Equivalent(f, s))) / nice.Count));
        return JsonSerializer.Serialize(new
        {
            score,
            matchedSkills = matched,
            missingSkills = missing,
            reason = missing.Count == 0
                ? "All required skills are backed by verified candidate facts."
                : $"Verified facts cover {matched.Count} skill(s); missing evidence for {string.Join(", ", missing)}."
        });
    }

    private static string ProposeResumeChanges(string input)
    {
        var sections = ParseSections(input);
        var matched = SplitList(sections.GetValueOrDefault("MATCHED_SKILLS", ""));
        var title = sections.GetValueOrDefault("JOB_TITLE", "the role");
        var company = sections.GetValueOrDefault("COMPANY", "the company");
        var paragraphs = sections.GetValueOrDefault("RESUME_PARAGRAPHS", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var summaryLine = paragraphs.FirstOrDefault(p => p.Length > 60) ?? "Senior Software Engineer with experience building enterprise applications";
        var skillsText = matched.Count == 0 ? "modern .NET technologies" : string.Join(", ", matched.Take(6));
        return JsonSerializer.Serialize(new
        {
            changes = new object[]
            {
                new
                {
                    section = "SUMMARY",
                    oldText = summaryLine,
                    newText = $"{summaryLine.TrimEnd('.')} targeting the {title} position at {company}, with verified experience in {skillsText}.",
                    evidence = matched.Select(s => $"CandidateFact:{s}").ToList()
                }
            }
        });
    }

    private static string CoverLetter(string input)
    {
        var s = ParseSections(input);
        var matched = SplitList(s.GetValueOrDefault("MATCHED_SKILLS", ""));
        var body = $"Dear Hiring Team at {s.GetValueOrDefault("COMPANY", "your company")},\n\n" +
                   $"I am writing to apply for the {s.GetValueOrDefault("JOB_TITLE", "advertised")} position. " +
                   $"My verified experience includes {string.Join(", ", matched.Take(6))}, which aligns directly with your requirements.\n\n" +
                   "I would welcome the opportunity to discuss how I can contribute to your team.\n\nKind regards,\n" + s.GetValueOrDefault("CANDIDATE_NAME", "The Candidate");
        return JsonSerializer.Serialize(new { subject = $"Application: {s.GetValueOrDefault("JOB_TITLE", "Role")} - {s.GetValueOrDefault("CANDIDATE_NAME", "Candidate")}", body });
    }

    private static string RecruiterEmail(string input)
    {
        var s = ParseSections(input);
        var body = $"Hello,\n\nI noticed the {s.GetValueOrDefault("JOB_TITLE", "open")} role at {s.GetValueOrDefault("COMPANY", "your company")} and believe my background in " +
                   $"{string.Join(", ", SplitList(s.GetValueOrDefault("MATCHED_SKILLS", "")).Take(4))} is a strong fit. Could we arrange a short call?\n\nBest regards,\n{s.GetValueOrDefault("CANDIDATE_NAME", "The Candidate")}";
        return JsonSerializer.Serialize(new { subject = $"Regarding {s.GetValueOrDefault("JOB_TITLE", "the role")} at {s.GetValueOrDefault("COMPANY", "your company")}", body });
    }

    private static Dictionary<string, string> ParseSections(string input)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        var buffer = new List<string>();
        foreach (var line in input.Split('\n'))
        {
            var match = SectionRegex().Match(line);
            if (match.Success)
            {
                if (current is not null) result[current] = string.Join("\n", buffer).Trim();
                current = match.Groups[1].Value;
                buffer.Clear();
                var rest = line[(match.Length)..].Trim();
                if (rest.Length > 0) buffer.Add(rest);
            }
            else if (current is not null)
            {
                buffer.Add(line);
            }
        }

        if (current is not null) result[current] = string.Join("\n", buffer).Trim();
        return result;
    }

    private static List<string> SplitList(string value) =>
        value.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(v => v.Length > 0).ToList();

    private static bool Equivalent(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
        || (a.Equals("LLMs", StringComparison.OrdinalIgnoreCase) && b.Equals("LLM", StringComparison.OrdinalIgnoreCase))
        || (a.Equals("LLM", StringComparison.OrdinalIgnoreCase) && b.Equals("LLMs", StringComparison.OrdinalIgnoreCase));

    private static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    [GeneratedRegex(@"\b(senior|junior|lead|principal|staff|mid-level|intern)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorityRegex();

    [GeneratedRegex(@"^###\s*([A-Z_]+)\s*:?")]
    private static partial Regex SectionRegex();
}
