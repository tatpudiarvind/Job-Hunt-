using System.Text;
using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Application.Abstractions;

public sealed record RuntimeSettings
{
    public ExecutionMode Mode { get; init; } = ExecutionMode.DEMO;
    public string LlmProvider { get; init; } = "Demo";
    public string LlmDisplayName { get; init; } = "Demo";
    public string LlmBaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string LlmApiKey { get; init; } = "";
    public string LlmModel { get; init; } = "gpt-4o-mini";
    public string MasterResumePath { get; init; } = "";
    public int QualificationThreshold { get; init; } = 60;
    public int ApprovalTtlHours { get; init; } = 24;
    /// <summary>Optional per-country master resumes (ISO code to DOCX path). Missing countries use <see cref="MasterResumePath"/>.</summary>
    public IReadOnlyDictionary<string, string> CountryResumePaths { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> RoleResumePaths { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ResumeLibraryEntry> ResumeLibrary { get; init; } = [];
    public IReadOnlyList<ArvindJobHunter.Domain.Entities.SiteSettings> Sites { get; init; } = [];
    /// <summary>Job-site search pages the user runs on demand in the visible browser. Never fetched in the background.</summary>
    public IReadOnlyList<SavedSearch> SavedSearches { get; init; } = [];

    public string MasterResumeFor(string? countryCode) =>
        countryCode is not null && CountryResumePaths.TryGetValue(countryCode, out var path) && !string.IsNullOrWhiteSpace(path) ? path : MasterResumePath;

    // Records print every property by default; the API key must never end up in a log line or exception message.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"Mode = {Mode}, LlmProvider = {LlmProvider}, LlmDisplayName = {LlmDisplayName}, LlmBaseUrl = {LlmBaseUrl}, ")
            .Append($"LlmApiKey = {(string.IsNullOrWhiteSpace(LlmApiKey) ? "<none>" : "***")}, LlmModel = {LlmModel}, MasterResumePath = {MasterResumePath}, ")
            .Append($"QualificationThreshold = {QualificationThreshold}, ApprovalTtlHours = {ApprovalTtlHours}");
        return true;
    }
}

public interface IRuntimeSettingsProvider
{
    Task<RuntimeSettings> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(RuntimeSettings settings, CancellationToken cancellationToken);
}

public sealed record SavedSearch(string Name, string Url);

public sealed record ResumeLibraryEntry(string Path, string Label, IReadOnlyList<string> Keywords, string Kind = ResumeLibraryEntry.ResumeKind)
{
    public const string ResumeKind = "RESUME";
    public const string CoverLetterKind = "COVER_LETTER";
    public bool IsCoverLetter => string.Equals(Kind, CoverLetterKind, StringComparison.OrdinalIgnoreCase);
}
