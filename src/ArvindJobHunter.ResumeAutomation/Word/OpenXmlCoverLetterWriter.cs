using ArvindJobHunter.Application.Abstractions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.ResumeAutomation.Word;

/// <summary>
/// Builds the cover letter from a copy of the resume template: styles, theme, fonts, margins and headers stay the same,
/// only the body text is replaced. The template itself is never opened for writing.
/// </summary>
public sealed class OpenXmlCoverLetterWriter : ICoverLetterDocumentWriter
{
    public Task<string> WriteAsync(string? templatePath, string outputPath, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Cover letter body is required.", nameof(body));
        if (templatePath is not null && string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The template is read-only; the output path must differ.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        var useTemplate = templatePath is not null && File.Exists(templatePath) &&
                          string.Equals(Path.GetExtension(templatePath), ".docx", StringComparison.OrdinalIgnoreCase);
        if (useTemplate)
        {
            File.Copy(templatePath!, outputPath, overwrite: true);
            using var document = WordprocessingDocument.Open(outputPath, isEditable: true);
            var docBody = document.MainDocumentPart!.Document.Body!;
            var runStyle = BodyRunProperties(docBody);
            var sectionProperties = docBody.Elements<SectionProperties>().LastOrDefault()?.CloneNode(true);
            docBody.RemoveAllChildren();
            foreach (var paragraph in Paragraphs(body, runStyle)) docBody.AppendChild(paragraph);
            if (sectionProperties is not null) docBody.AppendChild(sectionProperties);
            document.MainDocumentPart.Document.Save();
        }
        else
        {
            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body(Paragraphs(body, null)));
            main.Document.Save();
        }

        return Task.FromResult(outputPath);
    }

    /// <summary>Formatting of the most common run in ordinary paragraphs, ignoring headings and bold labels.</summary>
    private static RunProperties? BodyRunProperties(Body body) =>
        body.Descendants<Paragraph>()
            .Where(p => p.ParagraphProperties?.ParagraphStyleId is null && p.InnerText.Length > 40)
            .SelectMany(p => p.Elements<Run>())
            .Select(r => r.RunProperties)
            .Where(rp => rp is not null && rp.Bold is null)
            .GroupBy(rp => rp!.OuterXml)
            .OrderByDescending(g => g.Count())
            .Select(g => (RunProperties)g.First()!.CloneNode(true))
            .FirstOrDefault();

    private static IEnumerable<Paragraph> Paragraphs(string body, RunProperties? runStyle)
    {
        foreach (var block in body.Replace("\r\n", "\n").Split("\n\n"))
        {
            var paragraph = new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "200" }));
            var lines = block.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var run = new Run();
                if (runStyle is not null) run.AppendChild((RunProperties)runStyle.CloneNode(true));
                if (i > 0) run.AppendChild(new Break());
                run.AppendChild(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
                paragraph.AppendChild(run);
            }
            yield return paragraph;
        }
    }
}
