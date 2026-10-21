namespace ArvindJobHunter.Application.Abstractions;

/// <summary>How closely a tailored resume keeps the master resume's template.</summary>
public sealed record TemplateFidelityReport(
    bool TemplatePreserved,
    IReadOnlyList<string> Issues,
    int MasterParagraphs,
    int TailoredParagraphs,
    int MasterPageEstimate,
    int TailoredPageEstimate,
    IReadOnlyList<string> MasterSections,
    IReadOnlyList<string> TailoredSections);

public interface ITemplateFidelityChecker
{
    /// <summary>Compares styles, fonts, section order, paragraph count and page estimate of two documents.</summary>
    TemplateFidelityReport Compare(string masterPath, string tailoredPath);
}
