using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Jobs;

/// <summary>
/// Read-only fetch of a public job posting. Extracts schema.org JobPosting JSON-LD when present,
/// otherwise falls back to title/meta/body text. Blocks non-HTTP schemes and private/loopback hosts.
/// </summary>
public sealed partial class HttpJobPostingFetcher(HttpClient http) : IJobPostingFetcher
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxDescriptionChars = 20_000;

    public async Task<JobPostingDraft> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https")) throw new ArgumentException("Only http/https URLs are supported.");
        if (IsPrivateHost(url.Host)) throw new ArgumentException("Local or private network addresses are not allowed.");

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 ArvindJobHunter/1.0 (local job tracker)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"The page returned {(int)response.StatusCode}.");

        var html = await ReadLimitedAsync(response, cancellationToken);
        var warnings = new List<string>();

        var fromJsonLd = TryExtractJsonLd(html);
        var title = fromJsonLd?.Title ?? Clean(Match(html, OgTitle()) ?? Match(html, TitleTag()));
        var company = fromJsonLd?.Company ?? Clean(Match(html, OgSiteName()));
        var location = fromJsonLd?.Location;
        var description = fromJsonLd?.Description ?? Clean(Match(html, MetaDescription()));

        if (string.IsNullOrWhiteSpace(description) || description.Length < 200)
        {
            var bodyText = ExtractBodyText(html);
            if (bodyText.Length > (description?.Length ?? 0)) description = bodyText;
            warnings.Add("Structured job data was not found; the description was extracted from page text and may need editing.");
        }
        if (string.IsNullOrWhiteSpace(title)) warnings.Add("Title could not be detected.");
        if (string.IsNullOrWhiteSpace(company)) warnings.Add("Company could not be detected.");
        if (string.IsNullOrWhiteSpace(description)) warnings.Add("No readable description found. The page may require login or JavaScript; paste the text manually.");

        if (description is { Length: > MaxDescriptionChars }) description = description[..MaxDescriptionChars];

        return new JobPostingDraft(url.ToString(), title, company, location, description ?? "", url.Host, warnings);
    }

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) break;
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed record JsonLdPosting(string? Title, string? Company, string? Location, string? Description);

    private static JsonLdPosting? TryExtractJsonLd(string html)
    {
        foreach (System.Text.RegularExpressions.Match m in JsonLdScript().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                var posting = FindJobPosting(doc.RootElement);
                if (posting is null) continue;
                var p = posting.Value;
                return new JsonLdPosting(
                    Str(p, "title"),
                    p.TryGetProperty("hiringOrganization", out var org) ? (org.ValueKind == JsonValueKind.Object ? Str(org, "name") : org.GetString()) : null,
                    ExtractLocation(p),
                    Clean(Str(p, "description")));
            }
            catch (JsonException) { /* malformed block; try the next one */ }
        }
        return null;
    }

    private static JsonElement? FindJobPosting(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "JobPosting") return element;
                if (element.TryGetProperty("@graph", out var g)) return FindJobPosting(g);
                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) if (FindJobPosting(item) is { } found) return found;
                return null;
            default: return null;
        }
    }

    private static string? ExtractLocation(JsonElement posting)
    {
        if (!posting.TryGetProperty("jobLocation", out var loc)) return null;
        if (loc.ValueKind == JsonValueKind.Array) loc = loc.EnumerateArray().FirstOrDefault();
        if (loc.ValueKind != JsonValueKind.Object || !loc.TryGetProperty("address", out var addr) || addr.ValueKind != JsonValueKind.Object) return null;
        var parts = new[] { Str(addr, "addressLocality"), Str(addr, "addressRegion"), Str(addr, "addressCountry") }.Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Match(string html, Regex regex) => regex.Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    private static string ExtractBodyText(string html)
    {
        var body = Match(html, BodyTag()) ?? html;
        body = ScriptsAndStyles().Replace(body, " ");
        return Clean(body) ?? "";
    }

    private static string? Clean(string? value)
    {
        if (value is null) return null;
        var text = Tags().Replace(value, " ");
        text = WebUtility.HtmlDecode(text);
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsPrivateHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
        var b = ip.MapToIPv4().GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    [GeneratedRegex("""<script[^>]*type\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdScript();
    [GeneratedRegex("""<meta[^>]*property\s*=\s*["']og:title["'][^>]*content\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitle();
    [GeneratedRegex("""<meta[^>]*property\s*=\s*["']og:site_name["'][^>]*content\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OgSiteName();
    [GeneratedRegex("""<meta[^>]*name\s*=\s*["']description["'][^>]*content\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaDescription();
    [GeneratedRegex("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
    [GeneratedRegex("<body[^>]*>(.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BodyTag();
    [GeneratedRegex("<(script|style|noscript|svg|nav|footer|header)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptsAndStyles();
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
