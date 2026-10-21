using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.ResumeAutomation.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.IntegrationTests;

public sealed class OpenXmlTemplateFidelityCheckerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-fidelity-{Guid.NewGuid():N}");

    public OpenXmlTemplateFidelityCheckerTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private string Write(string name, params Paragraph[] paragraphs)
    {
        var path = Path.Combine(directory, name);
        using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        main.Document = new Document(new Body(paragraphs));
        main.Document.Save();
        return path;
    }

    private static Paragraph Heading(string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }), new Run(new Text(text)));

    private static Paragraph Body(string text, string font = "Calibri") =>
        new(new Run(new RunProperties(new RunFonts { Ascii = font, HighAnsi = font }), new Text(text)));

    [Fact]
    public async Task TailoredCopyFromWordMaster_IsReportedAsPreserved()
    {
        var master = Write("master.docx", Heading("Experience"), Body("Built APIs in C#"), Heading("Skills"), Body("C#, Azure"));
        var output = Path.Combine(directory, "tailored.docx");
        await new OpenXmlResumeDocumentService().ApplyChangesAsync(master, output,
            [new ResumeChange("Skills", "C#, Azure", "C#, Azure, Kubernetes", ["Job asks for Kubernetes"])], CancellationToken.None);

        var report = new OpenXmlTemplateFidelityChecker().Compare(master, output);

        Assert.True(report.TemplatePreserved, string.Join("; ", report.Issues));
        Assert.Equal(["Experience", "Skills"], report.TailoredSections);
    }

    [Fact]
    public void LostHeadingStyleAndFont_AreReported()
    {
        var master = Write("master.docx", Heading("Experience"), Body("Built APIs"), Heading("Skills"), Body("C#"));
        var tailored = Write("tailored.docx", Body("Experience", "Arial"), Body("Built APIs", "Arial"), Body("Skills", "Arial"), Body("C#", "Arial"));

        var report = new OpenXmlTemplateFidelityChecker().Compare(master, tailored);

        Assert.False(report.TemplatePreserved);
        Assert.Contains(report.Issues, i => i.Contains("Heading1"));
        Assert.Contains(report.Issues, i => i.Contains("Calibri"));
        Assert.Contains(report.Issues, i => i.Contains("Sections missing"));
    }

    [Fact]
    public void PdfMaster_IsReportedAsNotPreserved()
    {
        var tailored = Write("tailored.docx", Body("Experience"));

        var report = new OpenXmlTemplateFidelityChecker().Compare(Path.Combine(directory, "master.pdf"), tailored);

        Assert.False(report.TemplatePreserved);
        Assert.Contains(report.Issues, i => i.Contains("PDF"));
    }
}
