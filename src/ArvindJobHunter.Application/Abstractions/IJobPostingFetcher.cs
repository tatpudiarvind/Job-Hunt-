namespace ArvindJobHunter.Application.Abstractions;

public sealed record JobPostingDraft(
    string Url,
    string? Title,
    string? Company,
    string? Location,
    string Description,
    string Source,
    IReadOnlyList<string> Warnings);

/// <summary>Fetches a public job posting page and extracts a draft the user reviews before saving. Never persists.</summary>
public interface IJobPostingFetcher
{
    Task<JobPostingDraft> FetchAsync(Uri url, CancellationToken cancellationToken);
}
