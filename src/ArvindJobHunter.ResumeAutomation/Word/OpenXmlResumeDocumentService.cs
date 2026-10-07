using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ArvindJobHunter.ResumeAutomation.Word;

/// <summary>
/// Open XML based resume automation. Requires no Microsoft Word installation.
/// The master document is opened read-only and is never modified; every tailored resume is a copy.
/// Supports .docx masters (edited in place on the copy, formatting preserved) and .pdf masters
/// (text is extracted and the tailored copy is produced as a new .docx).
/// </summary>
public sealed class OpenXmlResumeDocumentService : IResumeDocumentService
{
    public static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Master resume not found.", path);
        if (IsPdf(path)) return Task.FromResult(new ResumeDocument(ReadPdfParagraphs(path)));

        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body ?? throw new InvalidOperationException("Document has no body.");
        var paragraphs = body.Descendants<Paragraph>()
            .Select((p, index) => new ResumeParagraph(index, p.InnerText, p.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .ToList();
        return Task.FromResult(new ResumeDocument(paragraphs));
    }

    public Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
    {
        if (!File.Exists(masterPath)) throw new FileNotFoundException("Master resume not found.", masterPath);
        if (string.Equals(Path.GetFullPath(masterPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The master resume is read-only; the output path must differ.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (IsPdf(masterPath)) return Task.FromResult(ApplyChangesFromPdf(masterPath, outputPath, changes, cancellationToken));

        File.Copy(masterPath, outputPath, overwrite: true);
        new FileInfo(outputPath).IsReadOnly = false;

        var applied = new List<ResumeChange>();
        var skipped = new List<ResumeChange>();

        using (var document = WordprocessingDocument.Open(outputPath, isEditable: true))
        {
            var body = document.MainDocumentPart?.Document.Body ?? throw new InvalidOperationException("Document has no body.");
            var paragraphs = body.Descendants<Paragraph>().ToList();

            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = string.IsNullOrWhiteSpace(change.OldText)
                    ? null
                    : paragraphs.FirstOrDefault(p => p.InnerText.Contains(change.OldText, StringComparison.Ordinal));

                if (target is null)
                {
                    skipped.Add(change);
                    continue;
                }

                ReplaceParagraphText(target, target.InnerText.Replace(change.OldText, change.NewText, StringComparison.Ordinal));
                applied.Add(change);
            }

            document.MainDocumentPart.Document.Save();
        }

        return Task.FromResult(new ResumeChangeReport(outputPath, applied, skipped));
    }

    private static void ReplaceParagraphText(Paragraph paragraph, string newText)
    {
        var runs = paragraph.Elements<Run>().ToList();
        var template = runs.FirstOrDefault()?.RunProperties?.CloneNode(true) as RunProperties;
        foreach (var run in runs) run.Remove();

        var replacement = new Run();
        if (template is not null) replacement.AppendChild(template);
        replacement.AppendChild(new Text(newText) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
        paragraph.AppendChild(replacement);
    }

    private static List<ResumeParagraph> ReadPdfParagraphs(string path)
    {
        using var pdf = PdfDocument.Open(path);
        var lines = new List<string>();
        foreach (var page in pdf.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page, addDoubleNewline: false);
            lines.AddRange(text.Split('\n').Select(l => l.TrimEnd('\r').Trim()));
        }

        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select((l, index) => new ResumeParagraph(index, l, null))
            .ToList();
    }

    private static ResumeChangeReport ApplyChangesFromPdf(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
    {
        var lines = ReadPdfParagraphs(masterPath).Select(p => p.Text).ToList();
        var applied = new List<ResumeChange>();
        var skipped = new List<ResumeChange>();

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = string.IsNullOrWhiteSpace(change.OldText)
                ? -1
                : lines.FindIndex(l => l.Contains(change.OldText, StringComparison.Ordinal));

            if (index < 0)
            {
                skipped.Add(change);
                continue;
            }

            lines[index] = lines[index].Replace(change.OldText, change.NewText, StringComparison.Ordinal);
            applied.Add(change);
        }

        using (var document = WordprocessingDocument.Create(outputPath, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var line in lines)
            {
                body.AppendChild(new Paragraph(new Run(new Text(line) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve })));
            }
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return new ResumeChangeReport(outputPath, applied, skipped);
    }
}
