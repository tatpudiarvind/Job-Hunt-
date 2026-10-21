using ArvindJobHunter.ResumeAutomation.Word;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace ArvindJobHunter.IntegrationTests;

public sealed class OfficeDocumentExporterTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-pdf-{Guid.NewGuid():N}");

    public OfficeDocumentExporterTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
    }

    [Fact]
    public async Task MissingSource_Throws()
    {
        var exporter = new OfficeDocumentExporter();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            exporter.ExportPdfAsync(Path.Combine(directory, "missing.docx"), Path.Combine(directory, "out.pdf"), CancellationToken.None));
    }

    [Fact]
    public async Task ExportsPdfWithHash_WhenAnEngineIsInstalled()
    {
        var exporter = new OfficeDocumentExporter();
        if (exporter.AvailableEngine is null) return; // Capability check: needs Word or LibreOffice on this machine.

        var docx = Path.Combine(directory, "resume.docx");
        using (var doc = WordprocessingDocument.Create(docx, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Arvind - Senior .NET Engineer")))));
            main.Document.Save();
        }
        var before = await File.ReadAllBytesAsync(docx);

        var result = await exporter.ExportPdfAsync(docx, Path.Combine(directory, "resume.pdf"), CancellationToken.None);

        Assert.True(File.Exists(result.PdfPath));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await File.ReadAllBytesAsync(result.PdfPath))[..4]));
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(before, await File.ReadAllBytesAsync(docx));
    }
}
