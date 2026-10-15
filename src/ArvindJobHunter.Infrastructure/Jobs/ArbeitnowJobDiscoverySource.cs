using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Jobs;

/// <summary>Europe-wide public job board (https://www.arbeitnow.com/api/job-board-api); free and keyless.</summary>
public sealed class ArbeitnowJobDiscoverySource(HttpClient http, ILogger<ArbeitnowJobDiscoverySource> logger) : IJobDiscoverySource
{
    private const int MaxPages = 5;
    private static readonly Regex TagsRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string Name => "Arbeitnow";

    public async Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken)
    {
        var jobs = new List<DiscoveredJobPosting>();
        for (var page = 1; page <= MaxPages; page++)
        {
            try
            {
                using var response = await http.GetAsync($"https://www.arbeitnow.com/api/job-board-api?page={page}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) break;
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (!doc.RootElement.TryGetProperty("data", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) break;
                foreach (var item in array.EnumerateArray())
                {
                    var posting = Parse(item);
                    if (posting is not null && LooksRelevant(posting, request)) jobs.Add(posting);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Arbeitnow page {Page} failed during discovery", page);
                break;
            }
        }

        return jobs;
    }

    private static DiscoveredJobPosting? Parse(JsonElement item)
    {
        var slug = Text(item, "slug");
        var title = Text(item, "title");
        var company = Text(item, "company_name");
        var url = Text(item, "url");
        var description = HtmlToText(Text(item, "description"));
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(description)) return null;

        var location = Text(item, "location") ?? "";
        if (item.TryGetProperty("remote", out var remote) && remote.ValueKind == JsonValueKind.True)
        {
            location = string.IsNullOrWhiteSpace(location) ? "Remote" : $"{location} (Remote)";
        }

        var tags = new List<string>();
        if (item.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
        {
            tags.AddRange(tagArray.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        DateTimeOffset? postedAt = item.TryGetProperty("created_at", out var created) && created.TryGetInt64(out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

        return new DiscoveredJobPosting(slug, title, company ?? "Unknown company", location, "Arbeitnow", url, description, tags, postedAt);
    }

    private static string? Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool LooksRelevant(DiscoveredJobPosting posting, JobDiscoveryRequest request)
    {
        var text = $"{posting.Title}\n{posting.Description}\n{string.Join(' ', posting.Tags)}";
        if (!string.IsNullOrWhiteSpace(request.CurrentRole) && text.Contains(request.CurrentRole, StringComparison.OrdinalIgnoreCase)) return true;
        if (request.PreferredRoles.Any(role => text.Contains(role, StringComparison.OrdinalIgnoreCase))) return true;
        return request.Skills.Count == 0 || request.Skills.Any(skill => text.Contains(skill, StringComparison.OrdinalIgnoreCase));
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var text = WebUtility.HtmlDecode(TagsRegex.Replace(html, " "));
        return WhitespaceRegex.Replace(text, " ").Trim();
    }
}
