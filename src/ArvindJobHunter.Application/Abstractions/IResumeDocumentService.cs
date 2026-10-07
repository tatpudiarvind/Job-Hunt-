using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

public sealed record ResumeDocument(IReadOnlyList<ResumeParagraph> Paragraphs);

public sealed record ResumeParagraph(int Index, string Text, string? Style);

public sealed record ResumeChangeReport(string OutputPath, IReadOnlyList<ResumeChange> Applied, IReadOnlyList<ResumeChange> Skipped);

/// <summary>Read-only master resume; every tailored document is a copy.</summary>
public interface IResumeDocumentService
{
    Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken);
    Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken);
}
