using System.Net.Http.Json;
using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Infrastructure.Browser;

/// <summary>Calls the loopback Worker's browser endpoints with the shared secret.</summary>
public sealed class WorkerBrowserApplyClient(HttpClient http) : IBrowserApplyClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<FillPreviewResponse> PrepareAsync(FillPreviewRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/apply/prepare", request, Json, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<FillPreviewResponse>(Json, cancellationToken))!;
    }

    public async Task BindAsync(string sessionId, string payloadHash, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/apply/bind", new { sessionId, payloadHash }, Json, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    public async Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/apply/submit", request, Json, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SubmitResponse>(Json, cancellationToken))!;
    }

    public async Task OpenAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/browser/open", new { url }, Json, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<DownloadedFile>> DownloadsAsync(CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<DownloadedFile>>("/browser/downloads", Json, cancellationToken) ?? [];

    public async Task<CapturedJobPage?> CaptureAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync("/browser/capture", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CapturedJobPage>(Json, cancellationToken);
    }

    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Browser worker returned {(int)response.StatusCode}: {body}");
    }
}
