using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Application.Abstractions;

/// <summary>The API's view of the Worker's visible browser. Implementations must never submit outside <see cref="SubmitAsync"/>.</summary>
public interface IBrowserApplyClient
{
    Task<FillPreviewResponse> PrepareAsync(FillPreviewRequest request, CancellationToken cancellationToken);
    Task BindAsync(string sessionId, string payloadHash, CancellationToken cancellationToken);
    Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken cancellationToken);
    Task OpenAsync(string url, CancellationToken cancellationToken);
    Task<IReadOnlyList<DownloadedFile>> DownloadsAsync(CancellationToken cancellationToken);
    Task<CapturedJobPage?> CaptureAsync(CancellationToken cancellationToken);
}

public sealed record CapturedJobPage(string Url, string Title, string PageTitle, string Text);

public sealed record DownloadedFile(string Path, string Name, long Size, DateTimeOffset Modified);

/// <summary>Per-site switches and daily caps (stored with the runtime settings).</summary>
public interface ISiteSettingsProvider
{
    Task<IReadOnlyList<SiteSettings>> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<SiteSettings> sites, CancellationToken cancellationToken);
}
