using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.ResumeAutomation.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.IntegrationTests;

public sealed class OpenXmlResumeDocumentServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-resume-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public async Task EditingALine_KeepsTheFormattingOfTheOtherRunsInThatLine()
    {
        Directory.CreateDirectory(directory);
        var master = Path.Combine(directory, "master.docx");
        using (var doc = WordprocessingDocument.Create(master, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(
                new Run(new RunProperties(new Bold()), new Text("Skills: ") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
                new Run(new Text("C#, Azure")))));
            main.Document.Save();
        }

        var output = Path.Combine(directory, "tailored.docx");
        var report = await new OpenXmlResumeDocumentService().ApplyChangesAsync(master, output,
            [new ResumeChange("Skills", "C#, Azure", "C#, Azure, Kubernetes", ["Job asks for Kubernetes"])], CancellationToken.None);

        Assert.Single(report.Applied);
        using var result = WordprocessingDocument.Open(output, false);
        var runs = result.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();
        Assert.Equal("Skills: C#, Azure, Kubernetes", string.Concat(runs.Select(r => r.InnerText)));
        Assert.NotNull(runs.First(r => r.InnerText == "Skills: ").RunProperties?.Bold);
        Assert.Null(runs.First(r => r.InnerText.Contains("Kubernetes")).RunProperties?.Bold);
    }

    private async Task<string> TailorAsync(Paragraph paragraph, ResumeChange change)
    {
        Directory.CreateDirectory(directory);
        var master = Path.Combine(directory, $"master-{Guid.NewGuid():N}.docx");
        using (var doc = WordprocessingDocument.Create(master, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraph));
            main.Document.Save();
        }

        var output = Path.Combine(directory, $"tailored-{Guid.NewGuid():N}.docx");
        var report = await new OpenXmlResumeDocumentService().ApplyChangesAsync(master, output, [change], CancellationToken.None);
        Assert.Single(report.Applied);
        return output;
    }

    private static Text Preserved(string value) => new(value) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve };

    [Fact]
    public async Task ChangeSpanningBoldAndPlainRuns_KeepsBoldLabelAndTrailingItalic()
    {
        var output = await TailorAsync(
            new Paragraph(
                new Run(new RunProperties(new Bold()), Preserved("Senior Engineer ")),
                new Run(Preserved("at Contoso")),
                new Run(new RunProperties(new Italic()), Preserved(" (2020-2024)"))),
            new ResumeChange("Experience", "Engineer at Contoso", "Engineer at Contoso Ltd", ["Exact company name"]));

        using var result = WordprocessingDocument.Open(output, false);
        var runs = result.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();
        Assert.Equal("Senior Engineer at Contoso Ltd (2020-2024)", string.Concat(runs.Select(r => r.InnerText)));
        Assert.NotNull(runs.First(r => r.InnerText.StartsWith("Senior")).RunProperties?.Bold);
        Assert.NotNull(runs.First(r => r.InnerText.Contains("2020")).RunProperties?.Italic);
    }

    [Fact]
    public async Task EditingTextBeforeAHyperlink_KeepsTheHyperlink()
    {
        var output = await TailorAsync(
            new Paragraph(
                new Run(Preserved("Portfolio for .NET work: ")),
                new Hyperlink(new Run(new RunProperties(new RunStyle { Val = "Hyperlink" }), Preserved("github.com/arvind"))) { Id = "rId99" }),
            new ResumeChange("Links", ".NET work", ".NET and Azure work", ["Job asks for Azure"]));

        using var result = WordprocessingDocument.Open(output, false);
        var hyperlink = Assert.Single(result.MainDocumentPart!.Document.Body!.Descendants<Hyperlink>());
        Assert.Equal("github.com/arvind", hyperlink.InnerText);
        Assert.Equal("Hyperlink", hyperlink.Descendants<RunStyle>().Single().Val?.Value);
        Assert.StartsWith("Portfolio for .NET and Azure work: ", result.MainDocumentPart.Document.Body.InnerText);
    }

    [Fact]
    public async Task TabSeparatedLine_KeepsTabAndDateFormatting()
    {
        var output = await TailorAsync(
            new Paragraph(
                new Run(new RunProperties(new Bold()), Preserved("Developer")),
                new Run(new TabChar()),
                new Run(new RunProperties(new Italic()), Preserved("2019 - 2021"))),
            new ResumeChange("Experience", "Developer", "Backend Developer", ["Role title"]));

        using var result = WordprocessingDocument.Open(output, false);
        var body = result.MainDocumentPart!.Document.Body!;
        Assert.Single(body.Descendants<TabChar>());
        var runs = body.Descendants<Run>().ToList();
        Assert.NotNull(runs.First(r => r.InnerText == "Backend Developer").RunProperties?.Bold);
        Assert.NotNull(runs.First(r => r.InnerText == "2019 - 2021").RunProperties?.Italic);
    }
}
