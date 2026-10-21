namespace ArvindJobHunter.Application.Abstractions;

/// <summary>Writes a cover letter as a .docx that reuses the fonts and page setup of a resume template.</summary>
public interface ICoverLetterDocumentWriter
{
    /// <param name="templatePath">A .docx resume whose styles to reuse; null or non-docx produces a plain document.</param>
    Task<string> WriteAsync(string? templatePath, string outputPath, string body, CancellationToken cancellationToken);
}
