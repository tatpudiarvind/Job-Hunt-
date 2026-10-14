using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Application.Features;

public sealed record ResumeAnalysisSummary(
    string SourcePath,
    string? Name,
    string? CurrentRole,
    string? CurrentCompany,
    int SkillFacts,
    int ExperienceFacts,
    int EducationFacts,
    int CertificationFacts,
    int PreferenceFacts,
    int TotalFacts,
    DateTimeOffset AnalyzedAt);

public sealed class ResumeAnalysisService(
    IResumeDocumentService documents,
    ILlmProviderResolver llmResolver,
    CandidateProfileService profiles,
    IRepository<CandidateFact> facts,
    IJobSearchPreferencesProvider preferences,
    AuditService audit,
    ILogger<ResumeAnalysisService> logger)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString };

    public async Task<ResumeAnalysisSummary> AnalyzeAndSaveAsync(string resumePath, Guid userId, CancellationToken cancellationToken)
    {
        var document = await documents.ReadAsync(resumePath, cancellationToken);
        var extracted = await TryExtractWithLlmAsync(document, cancellationToken) ?? HeuristicExtract(document);
        var fallback = HeuristicExtract(document);
        extracted = extracted.MergeMissingFrom(fallback);
        var linkedInProfileUrl = ExtractLinkedInUrl(document);

        var existingProfile = await profiles.GetCurrentAsync(cancellationToken);
        var name = FirstNonEmpty(extracted.Name, existingProfile?.Name);
        var currentRole = FirstNonEmpty(extracted.CurrentRole, existingProfile?.CurrentRole);
        var currentCompany = FirstNonEmpty(extracted.CurrentCompany, existingProfile?.CurrentCompany);

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(currentRole))
        {
            await profiles.UpdateAsync(CandidateProfileService.LocalCandidateId, name, currentRole, currentCompany, cancellationToken);
        }

        foreach (var fact in (await facts.ListAsync(cancellationToken)).Where(f => string.Equals(f.SourceType, "RESUME", StringComparison.OrdinalIgnoreCase)))
        {
            await facts.DeleteAsync(fact.Id, cancellationToken);
        }

        var created = new List<CandidateFact>();
        created.AddRange(await SaveFactsAsync("SKILL", extracted.Skills, resumePath, cancellationToken));
        created.AddRange(await SaveFactsAsync("EXPERIENCE", extracted.Experience, resumePath, cancellationToken));
        created.AddRange(await SaveFactsAsync("EDUCATION", extracted.Education, resumePath, cancellationToken));
        created.AddRange(await SaveFactsAsync("CERTIFICATION", extracted.Certifications, resumePath, cancellationToken));
        created.AddRange(await SaveFactsAsync("PREFERENCE", extracted.Preferences, resumePath, cancellationToken));

        var currentPreferences = await preferences.GetAsync(cancellationToken);
        await preferences.SaveAsync(currentPreferences with { ResumeLinkedInProfileUrl = linkedInProfileUrl }, cancellationToken);

        var summary = new ResumeAnalysisSummary(
            resumePath,
            name,
            currentRole,
            currentCompany,
            created.Count(f => f.FactType == "SKILL"),
            created.Count(f => f.FactType == "EXPERIENCE"),
            created.Count(f => f.FactType == "EDUCATION"),
            created.Count(f => f.FactType == "CERTIFICATION"),
            created.Count(f => f.FactType == "PREFERENCE"),
            created.Count,
            DateTimeOffset.UtcNow);

        logger.LogInformation("Resume analysis completed for {Resume}: {TotalFacts} fact(s) extracted (skills={Skills}, experience={Experience}, education={Education}, certifications={Certifications}, preferences={Preferences}, linkedIn={LinkedIn})",
            Path.GetFileName(resumePath), summary.TotalFacts, summary.SkillFacts, summary.ExperienceFacts, summary.EducationFacts, summary.CertificationFacts, summary.PreferenceFacts, linkedInProfileUrl ?? "missing");
        await audit.RecordAsync(userId, "MASTER_RESUME_ANALYZED", "RuntimeSettings", "local", "SUCCESS",
            $"{Path.GetFileName(resumePath)} => facts:{summary.TotalFacts}; profile:{summary.Name}/{summary.CurrentRole}; linkedin:{(linkedInProfileUrl ?? "missing")}", cancellationToken);
        return summary;
    }

    private async Task<IReadOnlyList<CandidateFact>> SaveFactsAsync(string factType, IReadOnlyList<string> values, string resumePath, CancellationToken cancellationToken)
    {
        var created = new List<CandidateFact>();
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fact = CandidateFact.Create(factType, value, value, Path.GetFileName(resumePath), "RESUME").SetVerification(true);
            await facts.UpsertAsync(fact, cancellationToken);
            created.Add(fact);
        }

        return created;
    }

    private async Task<ExtractedResume?> TryExtractWithLlmAsync(ResumeDocument document, CancellationToken cancellationToken)
    {
        try
        {
            var llm = await llmResolver.ResolveAsync(cancellationToken);
            var content = string.Join("\n", document.Paragraphs.Select(p => p.Text)).Trim();
            if (content.Length == 0) return null;
            if (content.Length > 8000) content = content[..8000];

            var prompt = "### RESUME_TEXT:\n" + content + "\n\n" +
                         "Return JSON: {\"name\":\"\",\"currentRole\":\"\",\"currentCompany\":\"\",\"skills\":[],\"experience\":[],\"education\":[],\"certifications\":[],\"preferences\":[]}";
            var response = await llm.CompleteAsync(new LlmRequest(
                "You extract structured candidate facts from a user-provided resume. Use only the resume text. Never invent facts. Respond with a single JSON object only.",
                [new LlmMessage("user", prompt)],
                "ResumeProfile"), cancellationToken);

            var parsed = ParseJson<ExtractedResumeDto>(response.Content);
            return new ExtractedResume(
                NormalizeOptional(parsed.Name),
                NormalizeOptional(parsed.CurrentRole),
                NormalizeOptional(parsed.CurrentCompany),
                CleanList(parsed.Skills),
                CleanList(parsed.Experience),
                CleanList(parsed.Education),
                CleanList(parsed.Certifications),
                CleanList(parsed.Preferences));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "Resume analysis fell back to heuristics because structured extraction failed.");
            return null;
        }
    }

    private static T ParseJson<T>(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        var json = start >= 0 && end > start ? content[start..(end + 1)] : content;
        return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("LLM returned an empty object.");
    }

    private static ExtractedResume HeuristicExtract(ResumeDocument document)
    {
        var lines = document.Paragraphs.Select(p => Normalize(p.Text)).Where(t => t.Length > 0).ToList();
        var sections = GroupSections(lines);
        var name = GuessName(lines);
        var role = GuessRole(lines, sections);
        var company = GuessCompany(sections);
        var skills = sections.TryGetValue("SKILLS", out var skillLines)
            ? SplitItems(skillLines)
            : DetectKnownSkills(string.Join("\n", lines));
        var experience = sections.TryGetValue("EXPERIENCE", out var experienceLines)
            ? TakeMeaningful(experienceLines, 6)
            : lines.Where(IsExperienceLine).Take(4).ToList();
        var education = sections.TryGetValue("EDUCATION", out var educationLines)
            ? TakeMeaningful(educationLines, 4)
            : lines.Where(IsEducationLine).Take(3).ToList();
        var certifications = sections.TryGetValue("CERTIFICATIONS", out var certificationLines)
            ? TakeMeaningful(certificationLines, 4)
            : lines.Where(IsCertificationLine).Take(3).ToList();
        var preferences = DetectPreferences(lines, sections);

        return new ExtractedResume(name, role, company, skills, experience, education, certifications, preferences);
    }

    private static Dictionary<string, List<string>> GroupSections(IReadOnlyList<string> lines)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string current = "HEADER";
        sections[current] = [];
        foreach (var line in lines)
        {
            if (TryMapSection(line, out var section))
            {
                current = section;
                if (!sections.ContainsKey(current)) sections[current] = [];
                continue;
            }

            sections[current].Add(line);
        }

        return sections;
    }

    private static bool TryMapSection(string line, out string section)
    {
        var normalized = Regex.Replace(line, "[^A-Za-z ]", "").Trim().ToUpperInvariant();
        section = normalized switch
        {
            "SKILLS" or "TECHNICAL SKILLS" or "CORE SKILLS" or "KEY SKILLS" => "SKILLS",
            "EXPERIENCE" or "WORK EXPERIENCE" or "PROFESSIONAL EXPERIENCE" or "EMPLOYMENT HISTORY" => "EXPERIENCE",
            "EDUCATION" or "ACADEMIC BACKGROUND" => "EDUCATION",
            "CERTIFICATIONS" or "CERTIFICATES" or "LICENSES" => "CERTIFICATIONS",
            "PREFERENCES" or "JOB PREFERENCES" or "WORK AUTHORIZATION" => "PREFERENCES",
            _ => ""
        };
        return section.Length > 0;
    }

    private static string? GuessName(IReadOnlyList<string> lines) =>
        lines.Take(3).FirstOrDefault(line => line.Length is > 3 and < 80 && !line.Contains('@') && !line.Contains("http", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(line, "\\d{3,}"));

    private static string? GuessRole(IReadOnlyList<string> lines, IReadOnlyDictionary<string, List<string>> sections)
    {
        if (sections.TryGetValue("HEADER", out var header))
        {
            var role = header.Skip(1).FirstOrDefault(line => line.Length is > 3 and < 120 && !line.Contains('@'));
            if (!string.IsNullOrWhiteSpace(role)) return role;
        }

        return lines.Skip(1).Take(4).FirstOrDefault(line => line.Length is > 3 and < 120 && !line.Contains('@'));
    }

    private static string? GuessCompany(IReadOnlyDictionary<string, List<string>> sections)
    {
        if (!sections.TryGetValue("EXPERIENCE", out var experience)) return null;
        foreach (var line in experience)
        {
            var match = Regex.Match(line, @"\bat\s+([^,|\-]+)", RegexOptions.IgnoreCase);
            if (match.Success) return NormalizeOptional(match.Groups[1].Value);
        }

        return null;
    }

    private static List<string> DetectKnownSkills(string text) => KnownSkills.Where(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase)).ToList();

    private static List<string> DetectPreferences(IReadOnlyList<string> lines, IReadOnlyDictionary<string, List<string>> sections)
    {
        var values = new List<string>();
        if (sections.TryGetValue("PREFERENCES", out var preferenceLines)) values.AddRange(TakeMeaningful(preferenceLines, 5));

        var fullText = string.Join("\n", lines);
        foreach (var keyword in new[] { "Remote", "Hybrid", "On-site", "UK", "United Kingdom", "Europe", "European Union", "Visa Sponsorship", "Relocation" })
        {
            if (fullText.Contains(keyword, StringComparison.OrdinalIgnoreCase)) values.Add(keyword);
        }

        return values.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsExperienceLine(string line) =>
        Regex.IsMatch(line, @"\b(20\d{2}|19\d{2}|present|current)\b", RegexOptions.IgnoreCase)
        || line.Contains(" at ", StringComparison.OrdinalIgnoreCase)
        || line.Contains('|');

    private static bool IsEducationLine(string line) =>
        line.Contains("Bachelor", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Master", StringComparison.OrdinalIgnoreCase)
        || line.Contains("University", StringComparison.OrdinalIgnoreCase)
        || line.Contains("College", StringComparison.OrdinalIgnoreCase)
        || line.Contains("B.Tech", StringComparison.OrdinalIgnoreCase)
        || line.Contains("BSc", StringComparison.OrdinalIgnoreCase)
        || line.Contains("MSc", StringComparison.OrdinalIgnoreCase);

    private static bool IsCertificationLine(string line) =>
        line.Contains("cert", StringComparison.OrdinalIgnoreCase)
        || line.Contains("aws", StringComparison.OrdinalIgnoreCase)
        || line.Contains("azure", StringComparison.OrdinalIgnoreCase)
        || line.Contains("scrum", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractLinkedInUrl(ResumeDocument document)
    {
        var text = string.Join("\n", document.Paragraphs.Select(p => p.Text));
        var match = Regex.Match(text, @"https?://(?:[a-z]{2,3}\.)?(?:www\.)?(?:linkedin\.com|lnkd\.in)/[^\s)>,]+", RegexOptions.IgnoreCase);
        return match.Success ? NormalizeOptional(match.Value.TrimEnd('.', ';', ',', ')')) : null;
    }

    private static List<string> TakeMeaningful(IEnumerable<string> lines, int max) =>
        lines.Select(Normalize).Where(line => line.Length > 3).Take(max).ToList();

    private static List<string> SplitItems(IEnumerable<string> lines) =>
        lines.SelectMany(line => line.Split([',', ';', '|', '•'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(Normalize)
            .Where(item => item.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<string> CleanList(IEnumerable<string>? values) =>
        values?.Select(Normalize).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

    private static string Normalize(string value) => Regex.Replace(value, "\\s+", " ").Trim().Trim('-', '•', '|');
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static readonly string[] KnownSkills =
    [
        "C#", ".NET", "ASP.NET Core", "Azure", "AWS", "SQL", "SQL Server", "PostgreSQL", "Docker", "Kubernetes", "Microservices",
        "REST", "gRPC", "Entity Framework", "Angular", "React", "TypeScript", "JavaScript", "Python", "Java", "Redis", "Kafka",
        "RabbitMQ", "CI/CD", "Git", "DevOps", "Terraform", "Blazor", "WPF", "OpenAI", "LLM", "LLMs", "RAG"
    ];

    private sealed record ExtractedResumeDto(
        string? Name,
        string? CurrentRole,
        string? CurrentCompany,
        List<string>? Skills,
        List<string>? Experience,
        List<string>? Education,
        List<string>? Certifications,
        List<string>? Preferences);

    private sealed record ExtractedResume(
        string? Name,
        string? CurrentRole,
        string? CurrentCompany,
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> Experience,
        IReadOnlyList<string> Education,
        IReadOnlyList<string> Certifications,
        IReadOnlyList<string> Preferences)
    {
        public ExtractedResume MergeMissingFrom(ExtractedResume fallback) => new(
            FirstNonEmpty(Name, fallback.Name),
            FirstNonEmpty(CurrentRole, fallback.CurrentRole),
            FirstNonEmpty(CurrentCompany, fallback.CurrentCompany),
            Skills.Count == 0 ? fallback.Skills : Skills,
            Experience.Count == 0 ? fallback.Experience : Experience,
            Education.Count == 0 ? fallback.Education : Education,
            Certifications.Count == 0 ? fallback.Certifications : Certifications,
            Preferences.Count == 0 ? fallback.Preferences : Preferences);
    }
}
