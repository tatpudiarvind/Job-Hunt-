using System.Text.RegularExpressions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Features;

/// <summary>A resume the user can upload, with what it is good for.</summary>
public sealed record ResumeCandidate(string Path, string Label, IReadOnlyList<string> Keywords);

public sealed record ResumeChoice(string Path, string Reason);

/// <summary>
/// Picks the file to upload for a job: (1) a GENERATED tailored version for this job, (2) the user's manual
/// role-to-resume mapping, (3) the best keyword score across the user's resumes, (4) the master resume.
/// PDF is preferred when one exists because every site accepts it and it keeps the layout.
/// </summary>
public static class ResumeSelector
{
    public static ResumeChoice? Select(
        Job job,
        IEnumerable<ResumeVersion> versions,
        IReadOnlyDictionary<string, string> roleMapping,
        IReadOnlyList<ResumeCandidate> candidates,
        string? masterPath,
        Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        var tailored = versions
            .Where(v => v.JobId == job.Id && v.Status == ResumeVersionStatus.GENERATED)
            .OrderByDescending(v => v.UpdatedAt)
            .FirstOrDefault();
        if (tailored is not null && Prefer(tailored.PdfPath, tailored.OutputPath, exists) is { } tailoredPath)
            return new ResumeChoice(tailoredPath, "Tailored resume generated for this job.");

        foreach (var (role, path) in roleMapping.OrderByDescending(m => m.Key.Length))
        {
            if (!string.IsNullOrWhiteSpace(role) && Contains(job.Title, role) && Prefer(PdfSibling(path), path, exists) is { } mapped)
                return new ResumeChoice(mapped, $"Mapped by role \"{role}\".");
        }

        var text = $"{job.Title}\n{job.Description}\n{string.Join(' ', job.Analysis?.RequiredSkills ?? [])}";
        var best = candidates
            .Select(c => (Candidate: c, Score: c.Keywords.Count(k => Contains(text, k)) + c.Keywords.Count(k => Contains(job.Title, k))))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();
        if (best.Candidate is not null && Prefer(PdfSibling(best.Candidate.Path), best.Candidate.Path, exists) is { } scored)
            return new ResumeChoice(scored, $"Best keyword match: {best.Candidate.Label} (score {best.Score}).");

        return !string.IsNullOrWhiteSpace(masterPath) && Prefer(PdfSibling(masterPath), masterPath, exists) is { } master
            ? new ResumeChoice(master, "Master resume (no tailored, mapped or keyword match).")
            : null;
    }

    private static string? Prefer(string? pdf, string? other, Func<string, bool> exists) =>
        !string.IsNullOrWhiteSpace(pdf) && exists(pdf) ? pdf : !string.IsNullOrWhiteSpace(other) && exists(other) ? other : null;

    private static string PdfSibling(string path) => Path.ChangeExtension(path, ".pdf");

    private static bool Contains(string text, string term) =>
        Regex.IsMatch(text, $@"(?<![\w]){Regex.Escape(term.Trim())}(?![\w])", RegexOptions.IgnoreCase);
}
