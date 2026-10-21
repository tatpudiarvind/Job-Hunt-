using ArvindJobHunter.Application.Abstractions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.ResumeAutomation.Word;

/// <summary>
/// Compares a master resume with a tailored copy and reports anything that changed the template:
/// styles or fonts that disappeared, sections that moved or vanished, a large change in paragraph count,
/// or a longer page estimate.
/// </summary>
public sealed class OpenXmlTemplateFidelityChecker : ITemplateFidelityChecker
{
    private const int CharactersPerPage = 2800;
    private const int ParagraphTolerance = 2;

    public TemplateFidelityReport Compare(string masterPath, string tailoredPath)
    {
        if (OpenXmlResumeDocumentService.IsPdf(masterPath))
        {
            var tailoredOnly = Snapshot.Read(tailoredPath);
            return new TemplateFidelityReport(false,
                ["The master resume is a PDF, so its layout could not be kept. Upload the original .docx to preserve your template."],
                0, tailoredOnly.Paragraphs, 0, tailoredOnly.PageEstimate, [], tailoredOnly.Sections);
        }

        var master = Snapshot.Read(masterPath);
        var tailored = Snapshot.Read(tailoredPath);
        var issues = new List<string>();

        var lostStyles = master.Styles.Except(tailored.Styles, StringComparer.OrdinalIgnoreCase).ToList();
        if (lostStyles.Count > 0) issues.Add($"Paragraph styles no longer used: {string.Join(", ", lostStyles)}.");

        var lostFonts = master.Fonts.Except(tailored.Fonts, StringComparer.OrdinalIgnoreCase).ToList();
        if (lostFonts.Count > 0) issues.Add($"Fonts no longer used: {string.Join(", ", lostFonts)}.");
        var newFonts = tailored.Fonts.Except(master.Fonts, StringComparer.OrdinalIgnoreCase).ToList();
        if (newFonts.Count > 0) issues.Add($"Fonts not in your template: {string.Join(", ", newFonts)}.");

        if (!master.Sections.SequenceEqual(tailored.Sections, StringComparer.OrdinalIgnoreCase))
        {
            var missing = master.Sections.Except(tailored.Sections, StringComparer.OrdinalIgnoreCase).ToList();
            issues.Add(missing.Count > 0
                ? $"Sections missing: {string.Join(", ", missing)}."
                : "Section order differs from your template.");
        }

        if (Math.Abs(master.Paragraphs - tailored.Paragraphs) > ParagraphTolerance)
            issues.Add($"Paragraph count changed from {master.Paragraphs} to {tailored.Paragraphs}.");

        if (tailored.PageEstimate > master.PageEstimate)
            issues.Add($"Estimated length grew from {master.PageEstimate} to {tailored.PageEstimate} page(s).");

        return new TemplateFidelityReport(issues.Count == 0, issues, master.Paragraphs, tailored.Paragraphs,
            master.PageEstimate, tailored.PageEstimate, master.Sections, tailored.Sections);
    }

    private sealed record Snapshot(HashSet<string> Styles, HashSet<string> Fonts, List<string> Sections, int Paragraphs, int PageEstimate)
    {
        public static Snapshot Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Resume document not found.", path);
            using var document = WordprocessingDocument.Open(path, isEditable: false);
            var main = document.MainDocumentPart ?? throw new InvalidOperationException("Document has no main part.");
            var body = main.Document.Body ?? throw new InvalidOperationException("Document has no body.");

            var paragraphs = body.Descendants<Paragraph>().Where(p => !string.IsNullOrWhiteSpace(p.InnerText)).ToList();
            var styles = paragraphs
                .Select(p => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fonts = body.Descendants<RunFonts>()
                .SelectMany(f => new[] { f.Ascii?.Value, f.HighAnsi?.Value })
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(f => f!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var sections = paragraphs.Where(IsSectionHeading).Select(p => p.InnerText.Trim()).ToList();

            var characters = paragraphs.Sum(p => p.InnerText.Length);
            var breaks = body.Descendants<Break>().Count(b => b.Type?.Value == BreakValues.Page)
                         + body.Descendants<LastRenderedPageBreak>().Count();
            var pages = Math.Max(breaks + 1, (int)Math.Ceiling(characters / (double)CharactersPerPage));

            return new Snapshot(styles, fonts, sections, paragraphs.Count, Math.Max(1, pages));
        }

        private static bool IsSectionHeading(Paragraph paragraph)
        {
            var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (style is not null && (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || style.Equals("Title", StringComparison.OrdinalIgnoreCase)))
                return true;

            // Many resumes format section titles as short, fully bold lines instead of using heading styles.
            var text = paragraph.InnerText.Trim();
            if (text.Length is 0 or > 40) return false;
            var runs = paragraph.Descendants<Run>().Where(r => !string.IsNullOrWhiteSpace(r.InnerText)).ToList();
            return runs.Count > 0 && runs.All(r => r.RunProperties?.Bold is { } bold && (bold.Val is null || bold.Val.Value));
        }
    }
}
