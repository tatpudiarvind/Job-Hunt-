using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Infrastructure.Resume;

public sealed class WorkerOptions
{
    /// <summary>Loopback base URL of the document worker, e.g. http://127.0.0.1:5310. Empty disables the proxy.</summary>
    public string BaseUrl { get; init; } = "";
    public string SharedSecret { get; init; } = "";
    public bool Enabled => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(SharedSecret);
}

/// <summary>
/// Delegates resume reads/writes to the out-of-process worker so document handling runs in an isolated process.
/// Falls back to the in-process Open XML service for reads if the worker is unreachable; never for writes.
/// </summary>
public sealed class WorkerResumeDocumentService(HttpClient http, WorkerOptions options, IResumeDocumentService inProcessFallback) : IResumeDocumentService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<ResumeDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("/resume/read", new { masterPath = path }, Json, cancellationToken);
            await ThrowIfFailedAsync(response, cancellationToken);
            return (await response.Content.ReadFromJsonAsync<ResumeDocument>(Json, cancellationToken))!;
        }
        catch (HttpRequestException)
        {
            return await inProcessFallback.ReadAsync(path, cancellationToken);
        }
    }

    public async Task<ResumeChangeReport> ApplyChangesAsync(string masterPath, string outputPath, IReadOnlyList<ResumeChange> changes, CancellationToken cancellationToken)
    {
        var payload = new
        {
            idempotencyKey = Path.GetFileNameWithoutExtension(outputPath),
            masterPath,
            outputFileName = Path.GetFileName(outputPath),
            changes
        };
        using var response = await http.PostAsJsonAsync("/resume/apply", payload, Json, cancellationToken);
        await ThrowIfFailedAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeChangeReport>(Json, cancellationToken))!;
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = $"Worker returned {(int)response.StatusCode}: {body}";
        throw response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => new FileNotFoundException(message),
            System.Net.HttpStatusCode.Conflict => new InvalidOperationException(message),
            System.Net.HttpStatusCode.Unauthorized => new InvalidOperationException("Worker rejected the shared secret."),
            _ => new InvalidOperationException(message)
        };
    }
}
