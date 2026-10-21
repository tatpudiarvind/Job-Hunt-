namespace ArvindJobHunter.Application.Abstractions;

public sealed record PdfExportResult(string PdfPath, string Sha256, string Engine);

/// <summary>Converts Word documents to PDF (and PDF masters to Word) using a locally installed office engine.</summary>
public interface IDocumentExporter
{
    /// <summary>Name of the engine that will be used ("Word", "LibreOffice"), or null when none is installed.</summary>
    string? AvailableEngine { get; }

    Task<PdfExportResult> ExportPdfAsync(string docxPath, string pdfPath, CancellationToken cancellationToken);

    /// <summary>Converts a PDF to .docx with Microsoft Word. Layout may shift; returns false when Word is not installed.</summary>
    Task<bool> TryConvertPdfToDocxAsync(string pdfPath, string docxPath, CancellationToken cancellationToken);
}
