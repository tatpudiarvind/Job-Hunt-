using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ArvindJobHunter.ResumeAutomation.Word;

/// <summary>
/// Open XML based resume automation. Requires no Microsoft Word installation.
/// The master document is opened read-only and is never modified; every tailored resume is a copy.
/// Supports .docx masters (edited in place on the copy, formatting preserved) and .pdf masters
/// (text is extracted and the tailored copy is produced as a new .docx).
/// </summary>
public sealed class OpenXmlResumeDocumentService(ILogger<OpenXmlResumeDocumentService>? logger = null) : IResumeDocumentService
{
    public static bool IsPdf(string path) => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Master resume not found.", path);
        if (IsPdf(path))
        {
            var pdfParagraphs = ReadPdfParagraphs(path);
            logger?.LogInformation("Read {ParagraphCount} line(s) from PDF master resume {File}", pdfParagraphs.Count, Path.GetFileName(path));
            return Task.FromResult(new ResumeDocument(pdfParagraphs));
        }

        using var document = WordprocessingDocument.Open(path, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body ?? throw new InvalidOperationException("Document has no body.");
        var paragraphs = body.Descendants<Paragraph>()
            .Select((p, index) => new ResumeParagraph(index, p.InnerText, p.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
            .Where(p => !string.IsNullOrWhiteSpace(p.Text))
            .ToList();
        logger?.LogInformation("Read {ParagraphCount} paragraph(s) from Word master resume {File}", paragraphs.Count, Path.GetFileName(path));
        return Task.FromResult(new ResumeDocument(paragraphs));
    }

    public Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
    {
        if (!File.Exists(masterPath)) throw new FileNotFoundException("Master resume not found.", masterPath);
        if (string.Equals(Path.GetFullPath(masterPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The master resume is read-only; the output path must differ.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var report = IsPdf(masterPath)
            ? ApplyChangesFromPdf(masterPath, outputPath, changes, cancellationToken)
            : ApplyChangesToWordCopy(masterPath, outputPath, changes, cancellationToken);
        logger?.LogInformation("Tailored resume {Output} written from {Master}: {Applied} change(s) applied, {Skipped} skipped because their original text was not found",
            report.OutputPath, Path.GetFileName(masterPath), report.Applied.Count, report.Skipped.Count);
        foreach (var skipped in report.Skipped)
        {
            logger?.LogDebug("Skipped resume change in section {Section}: original text not found", skipped.Section);
        }

        return Task.FromResult(report);
    }

    private static ResumeChangeReport ApplyChangesToWordCopy(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
    {
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

                if (!ReplaceWithinRuns(target, change.OldText, change.NewText))
                    ReplaceParagraphText(target, target.InnerText.Replace(change.OldText, change.NewText, StringComparison.Ordinal));
                applied.Add(change);
            }

            document.MainDocumentPart.Document.Save();
        }

        return new ResumeChangeReport(outputPath, applied, skipped);
    }

    /// <summary>
    /// Replaces text only inside the runs it spans, so bold labels, links, tabs and other runs in the line keep their formatting.
    /// The new text takes the formatting of the run where the old text started.
    /// </summary>
    private static bool ReplaceWithinRuns(Paragraph paragraph, string oldText, string newText)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        var joined = string.Concat(texts.Select(t => t.Text));
        var start = joined.IndexOf(oldText, StringComparison.Ordinal);
        if (start < 0) return false;
        var end = start + oldText.Length;

        var offset = 0;
        var inserted = false;
        foreach (var node in texts)
        {
            var nodeStart = offset;
            var nodeEnd = offset + node.Text.Length;
            offset = nodeEnd;
            if (nodeEnd <= start || nodeStart >= end) continue;

            var cutFrom = Math.Max(start, nodeStart) - nodeStart;
            var cutTo = Math.Min(end, nodeEnd) - nodeStart;
            var replacement = inserted ? string.Empty : newText;
            node.Text = node.Text[..cutFrom] + replacement + node.Text[cutTo..];
            node.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
            inserted = true;
        }

        return inserted;
    }

    /// <summary>
    /// Fallback when the old text is not contiguous in the text nodes (for example, split by a tab or a field).
    /// Only the span that differs between the current and the new line text is rewritten, so every run outside
    /// that span (bold labels, links, dates) keeps its formatting.
    /// </summary>
    private static void ReplaceParagraphText(Paragraph paragraph, string newText)
    {
        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Count == 0)
        {
            paragraph.AppendChild(new Run(new Text(newText) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }));
            return;
        }

        var current = string.Concat(texts.Select(t => t.Text));
        var prefix = 0;
        var maxPrefix = Math.Min(current.Length, newText.Length);
        while (prefix < maxPrefix && current[prefix] == newText[prefix]) prefix++;
        var suffix = 0;
        while (suffix < current.Length - prefix && suffix < newText.Length - prefix
               && current[current.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;

        var start = prefix;
        var end = current.Length - suffix;
        var replacement = newText.Substring(prefix, newText.Length - suffix - prefix);
        if (start == end && replacement.Length == 0) return;

        var offset = 0;
        var inserted = false;
        Text? last = null;
        foreach (var node in texts)
        {
            var nodeStart = offset;
            var nodeEnd = offset + node.Text.Length;
            offset = nodeEnd;
            last = node;
            // A pure insertion (start == end) goes into the run where it begins, or the preceding run at a boundary.
            var touches = start == end
                ? !inserted && start >= nodeStart && start <= nodeEnd
                : nodeEnd > start && nodeStart < end;
            if (!touches) continue;

            var cutFrom = Math.Max(start, nodeStart) - nodeStart;
            var cutTo = Math.Min(end, nodeEnd) - nodeStart;
            node.Text = node.Text[..cutFrom] + (inserted ? string.Empty : replacement) + node.Text[cutTo..];
            node.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
            inserted = true;
        }

        if (!inserted && last is not null)
        {
            last.Text += replacement;
            last.Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve;
        }
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
