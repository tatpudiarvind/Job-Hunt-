using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.ResumeAutomation.Word;

/// <summary>
/// Exports .docx to PDF with Microsoft Word (COM, Windows only) when installed, otherwise with LibreOffice
/// (<c>soffice --headless</c>). Both render the document's own styles, so the PDF matches the Word template.
/// The source document is never modified.
/// </summary>
public sealed class OfficeDocumentExporter(ILogger<OfficeDocumentExporter>? logger = null, string? sofficePath = null) : IDocumentExporter
{
    private const int WdFormatPdf = 17;
    private const int WdFormatDocumentDefault = 16;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Lazy<bool> wordInstalled = new(() => OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Word.Application") is not null);
    private readonly Lazy<string?> soffice = new(() => sofficePath ?? FindSoffice());

    public string? AvailableEngine => wordInstalled.Value ? "Word" : soffice.Value is not null ? "LibreOffice" : null;

    public async Task<PdfExportResult> ExportPdfAsync(string docxPath, string pdfPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(docxPath)) throw new FileNotFoundException("Document to export not found.", docxPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pdfPath))!);

        var engine = AvailableEngine ?? throw new InvalidOperationException(
            "PDF export needs Microsoft Word or LibreOffice installed on this machine.");

        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (engine == "Word") await Task.Run(() => RunWord(docxPath, pdfPath, WdFormatPdf, openPdf: false), cancellationToken);
            else await RunSofficeAsync(docxPath, pdfPath, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }

        if (!File.Exists(pdfPath)) throw new InvalidOperationException($"{engine} did not produce a PDF.");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pdfPath, cancellationToken))).ToLowerInvariant();
        logger?.LogInformation("Exported {Docx} to PDF with {Engine} ({Pdf}, sha256 {Hash})", Path.GetFileName(docxPath), engine, pdfPath, hash);
        return new PdfExportResult(pdfPath, hash, engine);
    }

    public async Task<bool> TryConvertPdfToDocxAsync(string pdfPath, string docxPath, CancellationToken cancellationToken)
    {
        if (!wordInstalled.Value) return false;
        if (!File.Exists(pdfPath)) throw new FileNotFoundException("PDF not found.", pdfPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(docxPath))!);

        await Gate.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() => RunWord(pdfPath, docxPath, WdFormatDocumentDefault, openPdf: true), cancellationToken);
        }
        finally
        {
            Gate.Release();
        }

        logger?.LogInformation("Converted PDF {Pdf} to Word {Docx}; layout may differ from the PDF", Path.GetFileName(pdfPath), docxPath);
        return File.Exists(docxPath);
    }

    private static void RunWord(string sourcePath, string targetPath, int format, bool openPdf)
    {
        var type = Type.GetTypeFromProgID("Word.Application") ?? throw new InvalidOperationException("Microsoft Word is not installed.");
        dynamic? word = null;
        dynamic? document = null;
        try
        {
            word = Activator.CreateInstance(type)!;
            word.Visible = false;
            word.DisplayAlerts = 0;
            // ConfirmConversions=false, ReadOnly=true: the source is never modified.
            document = word.Documents.Open(Path.GetFullPath(sourcePath), false, true, false);
            document.SaveAs2(Path.GetFullPath(targetPath), format);
        }
        finally
        {
            if (document is not null)
            {
                try { document.Close(false); } catch { }
                Marshal.FinalReleaseComObject(document);
            }
            if (word is not null)
            {
                try { word.Quit(false); } catch { }
                Marshal.FinalReleaseComObject(word);
            }
        }
    }

    private async Task RunSofficeAsync(string docxPath, string pdfPath, CancellationToken cancellationToken)
    {
        var outDir = Path.Combine(Path.GetTempPath(), "JobHunter", "pdf-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            var start = new ProcessStartInfo(soffice.Value!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", outDir, Path.GetFullPath(docxPath) })
                start.ArgumentList.Add(arg);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("LibreOffice could not be started.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                throw;
            }

            var produced = Path.Combine(outDir, Path.GetFileNameWithoutExtension(docxPath) + ".pdf");
            if (process.ExitCode != 0 || !File.Exists(produced))
                throw new InvalidOperationException($"LibreOffice PDF export failed (exit {process.ExitCode}): {await process.StandardError.ReadToEndAsync(cancellationToken)}");
            File.Copy(produced, pdfPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(outDir, true); } catch { }
        }
    }

    private static string? FindSoffice()
    {
        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "LibreOffice", "program", "soffice.exe"));
        }
        else
        {
            candidates.AddRange(["/usr/bin/soffice", "/usr/local/bin/soffice", "/Applications/LibreOffice.app/Contents/MacOS/soffice"]);
        }

        return candidates.FirstOrDefault(File.Exists);
    }
}
