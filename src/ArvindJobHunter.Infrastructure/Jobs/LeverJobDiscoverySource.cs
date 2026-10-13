using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Jobs;

public sealed class LeverJobDiscoverySource(HttpClient http, ILogger<LeverJobDiscoverySource> logger) : IJobDiscoverySource
{
    private static readonly Regex TagsRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string Name => "Lever";

    public async Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken)
    {
        var jobs = new List<DiscoveredJobPosting>();
        foreach (var board in SupportedCareerBoards.Lever)
        {
            var endpoint = $"https://api.lever.co/v0/postings/{board.AccountToken}?mode=json";
            try
            {
                using var response = await http.GetAsync(endpoint, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    logger.LogDebug("Lever account {Account} was not found", board.AccountToken);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var posting = Parse(board.Company, item);
                    if (posting is not null && LooksRelevant(posting, request)) jobs.Add(posting);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                logger.LogWarning(ex, "Lever account {Account} failed during discovery", board.AccountToken);
            }
        }

        return jobs;
    }

    private static DiscoveredJobPosting? Parse(string company, JsonElement item)
    {
        var id = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var title = item.TryGetProperty("text", out var titleEl) ? titleEl.GetString() : null;
        var url = item.TryGetProperty("hostedUrl", out var urlEl) ? urlEl.GetString() : null;
        var description = string.Join(" ", ReadStrings(item, "descriptionPlain", "description", "lists"));
        var location = item.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Object && categories.TryGetProperty("location", out var locEl)
            ? locEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(description)) return null;

        var tags = new List<string>();
        if (categories.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in categories.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    tags.Add(property.Value.GetString()!);
                }
            }
        }

        DateTimeOffset? postedAt = null;
        if (item.TryGetProperty("createdAt", out var createdEl))
        {
            postedAt = createdEl.ValueKind switch
            {
                JsonValueKind.Number when createdEl.TryGetInt64(out var unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs),
                JsonValueKind.String when DateTimeOffset.TryParse(createdEl.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        return new DiscoveredJobPosting(id, title.Trim(), company, location?.Trim() ?? string.Empty, "Lever", url.Trim(), Clean(description), tags, postedAt);
    }

    private static IEnumerable<string> ReadStrings(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var element)) continue;
            switch (element.ValueKind)
            {
                case JsonValueKind.String when !string.IsNullOrWhiteSpace(element.GetString()):
                    yield return HtmlToText(element.GetString());
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                    {
                        if (child.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in child.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                                {
                                    yield return HtmlToText(prop.Value.GetString());
                                }
                            }
                        }
                    }
                    break;
            }
        }
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

    private static string Clean(string value) => WhitespaceRegex.Replace(value, " ").Trim();
    private static bool ContainsPhrase(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
