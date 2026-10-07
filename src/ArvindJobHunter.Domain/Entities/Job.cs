namespace ArvindJobHunter.Domain.Entities;

public sealed record Job(
    Guid Id,
    string Title,
    string Company,
    string Location,
    string Source,
    string? Url,
    string Description,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JobAnalysis? Analysis = null,
    JobMatch? Match = null)
{
    public static Job Create(string title, string company, string location, string source, string? url, string description)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Job title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(company)) throw new ArgumentException("Company is required.", nameof(company));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        var now = DateTimeOffset.UtcNow;
        return new Job(Guid.NewGuid(), title.Trim(), company.Trim(), location.Trim(), string.IsNullOrWhiteSpace(source) ? "MANUAL" : source.Trim(), url, description.Trim(), JobStatus.DISCOVERED, now, now);
    }

    public Job WithAnalysis(JobAnalysis analysis) =>
        this with { Analysis = analysis, Status = JobStatus.ANALYZED, UpdatedAt = DateTimeOffset.UtcNow };

    public Job WithMatch(JobMatch match, int qualificationThreshold) =>
        this with
        {
            Match = match,
            Status = match.Score >= qualificationThreshold ? JobStatus.QUALIFIED : Status,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    public Job Dismiss() => this with { Status = JobStatus.DISMISSED, UpdatedAt = DateTimeOffset.UtcNow };
}

public sealed record JobAnalysis(
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> NiceToHaveSkills,
    string SeniorityLevel,
    string Summary,
    IReadOnlyList<string> Responsibilities,
    DateTimeOffset AnalyzedAt);

public sealed record JobMatch(
    int Score,
    IReadOnlyList<string> MatchedSkills,
    IReadOnlyList<string> MissingSkills,
    string Reason,
    DateTimeOffset MatchedAt);
