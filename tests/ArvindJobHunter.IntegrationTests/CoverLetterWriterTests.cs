using ArvindJobHunter.ResumeAutomation.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.IntegrationTests;

public sealed class CoverLetterWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("jh-cover").FullName;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task Write_ReusesTemplateFontsAndLeavesTemplateUntouched()
    {
        var template = Path.Combine(_dir, "resume.docx");
        using (var doc = WordprocessingDocument.Create(template, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Garamond" }), new Text("Built distributed systems for payments across five countries."))),
                new SectionProperties(new PageMargin { Top = 720 })));
        }
        var before = await File.ReadAllBytesAsync(template);

        var output = Path.Combine(_dir, "letter.docx");
        await new OpenXmlCoverLetterWriter().WriteAsync(template, output, "Dear Hiring Manager,\n\nI am applying.\n\nKind regards,\nArvind", CancellationToken.None);

        Assert.Equal(before, await File.ReadAllBytesAsync(template));
        using var result = WordprocessingDocument.Open(output, false);
        var body = result.MainDocumentPart!.Document.Body!;
        Assert.Equal(3, body.Elements<Paragraph>().Count());
        Assert.Contains("I am applying.", body.InnerText);
        Assert.DoesNotContain("distributed systems", body.InnerText);
        Assert.All(body.Descendants<Run>(), r => Assert.Equal("Garamond", r.RunProperties?.RunFonts?.Ascii?.Value));
        Assert.NotNull(body.Elements<SectionProperties>().Single().GetFirstChild<PageMargin>());
    }

    [Fact]
    public async Task Write_WithoutTemplate_CreatesPlainDocument()
    {
        var output = Path.Combine(_dir, "plain.docx");
        await new OpenXmlCoverLetterWriter().WriteAsync(null, output, "Hello", CancellationToken.None);
        using var result = WordprocessingDocument.Open(output, false);
        Assert.Equal("Hello", result.MainDocumentPart!.Document.Body!.InnerText);
    }
}
