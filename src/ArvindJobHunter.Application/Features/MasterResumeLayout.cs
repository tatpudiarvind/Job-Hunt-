namespace ArvindJobHunter.Application.Features;

/// <summary>
/// Explains whether tailored copies can keep the master resume's layout. Only a Word (.docx) master can be
/// edited in place on a copy; a PDF master is reduced to text and rebuilt as a plain document.
/// </summary>
public static class MasterResumeLayout
{
    public const string PdfWarning =
        "Your master resume is a PDF. A PDF's layout can't be edited, so tailored copies are rebuilt as a plain Word document " +
        "and lose your template (fonts, columns, styling). Upload the original .docx to keep your template.";

    public static bool IsPdf(string? path) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static string? WarningFor(string? masterPath) => IsPdf(masterPath) ? PdfWarning : null;
}
