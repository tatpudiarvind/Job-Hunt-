using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Jobs;

public sealed class GreenhouseJobDiscoverySource(HttpClient http, ILogger<GreenhouseJobDiscoverySource> logger) : IJobDiscoverySource
{
    private static readonly Regex TagsRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string Name => "Greenhouse";

    public async Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken)
    {
        var jobs = new List<DiscoveredJobPosting>();
        foreach (var board in SupportedCareerBoards.Greenhouse)
        {
            var endpoint = $"https://boards-api.greenhouse.io/v1/boards/{board.BoardToken}/jobs?content=true";
            try
            {
                using var response = await http.GetAsync(endpoint, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogDebug("Greenhouse board {Board} was not found", board.BoardToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (!doc.RootElement.TryGetProperty("jobs", out var array) || array.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in array.EnumerateArray())
                {
                    var posting = Parse(board.Company, item);
                    if (posting is not null && LooksRelevant(posting, request)) jobs.Add(posting);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Greenhouse board {Board} failed during discovery", board.BoardToken);
            }
        }

        return jobs;
    }

    private static DiscoveredJobPosting? Parse(string company, JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idEl) ? idEl.ToString() : null;
        var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        var url = item.TryGetProperty("absolute_url", out var urlEl) ? urlEl.GetString() : null;
        var location = item.TryGetProperty("location", out var locEl) && locEl.ValueKind == JsonValueKind.Object && locEl.TryGetProperty("name", out var nameEl)
            ? nameEl.GetString()
            : null;
        var content = item.TryGetProperty("content", out var contentEl) ? HtmlToText(contentEl.GetString()) : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(content)) return null;

        var tags = new List<string>();
        if (item.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Array)
        {
            foreach (var meta in metadata.EnumerateArray())
            {
                if (meta.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    tags.Add(value.GetString()!);
                }
            }
        }

        DateTimeOffset? postedAt = null;
        if (item.TryGetProperty("updated_at", out var updated) && updated.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(updated.GetString(), out var parsed)) postedAt = parsed;
        return new DiscoveredJobPosting(id, title.Trim(), company, location?.Trim() ?? string.Empty, "Greenhouse", url.Trim(), content.Trim(), tags, postedAt);
    }

    private static bool LooksRelevant(DiscoveredJobPosting posting, JobDiscoveryRequest request)
    {
        var text = $"{posting.Title}\n{posting.Description}\n{string.Join(' ', posting.Tags)}";
        if (!string.IsNullOrWhiteSpace(request.CurrentRole) && ContainsPhrase(text, request.CurrentRole)) return true;
        if (request.PreferredRoles.Any(role => ContainsPhrase(text, role))) return true;
        return request.Skills.Count == 0 || request.Skills.Any(skill => ContainsPhrase(text, skill));
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = TagsRegex.Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        return WhitespaceRegex.Replace(text, " ").Trim();
    }

    private static bool ContainsPhrase(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
