using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Infrastructure.Jobs;

/// <summary>Resolves a host name to IP addresses (injectable so tests do not depend on real DNS).</summary>
public delegate Task<IPAddress[]> HostAddressResolver(string host, CancellationToken cancellationToken);

/// <summary>
/// Read-only fetch of a public job posting. Extracts schema.org JobPosting JSON-LD when present,
/// otherwise falls back to title/meta/body text. Blocks non-HTTP schemes and local/private targets: every
/// redirect hop is followed manually and re-validated, and host names that resolve to private addresses are refused.
/// The primary handler must therefore have automatic redirects disabled.
/// </summary>
public sealed partial class HttpJobPostingFetcher(HttpClient http, ILogger<HttpJobPostingFetcher>? logger = null, HostAddressResolver? resolveHost = null) : IJobPostingFetcher
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxDescriptionChars = 20_000;
    private const int MaxRedirects = 5;

    private readonly HostAddressResolver resolve = resolveHost ?? ((host, ct) => Dns.GetHostAddressesAsync(host, ct));

    public async Task<JobPostingDraft> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var current = url;
        HttpResponseMessage? response = null;
        try
        {
            for (var hop = 0; ; hop++)
            {
                await EnsurePublicAsync(current, cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 ArvindJobHunter/1.0 (local job tracker)");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!IsRedirect(response.StatusCode)) break;

                var redirectTarget = response.Headers.Location;
                response.Dispose();
                response = null;
                if (redirectTarget is null) throw new InvalidOperationException("The page redirected without a target location.");
                if (hop >= MaxRedirects) throw new InvalidOperationException($"The page redirected more than {MaxRedirects} times.");
                var next = redirectTarget.IsAbsoluteUri && redirectTarget.Scheme != Uri.UriSchemeFile
                    ? redirectTarget
                    : new Uri(current, redirectTarget.OriginalString);
                logger?.LogDebug("Job import: {From} redirected to {To}", current, next);
                current = next;
            }

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

            logger?.LogInformation("Job import from {Host}: {Characters} description characters in {ElapsedMs} ms (structured data: {StructuredData}, title: {Title}, company: {Company}, warnings: {WarningCount})",
                url.Host, description?.Length ?? 0, watch.ElapsedMilliseconds, fromJsonLd is not null, title ?? "?", company ?? "?", warnings.Count);
            return new JobPostingDraft(url.ToString(), title, company, location, description ?? "", url.Host, warnings);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger?.LogWarning(ex, "Job import from {Url} failed after {ElapsedMs} ms", current, watch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>True for loopback, private, link-local, carrier-grade NAT, multicast, unspecified and IPv6 unique-local/site-local addresses.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] is 0 or 10 or 127 or >= 224
                || (b[0] == 100 && b[1] is >= 64 and <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 192 && b[1] == 0 && b[2] == 0));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
        }

        return false;
    }

    /// <summary>
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> for the fetcher's client. For direct connections it resolves the host once,
    /// rejects any non-public address and connects to exactly the checked addresses, so a host whose DNS answer changes between
    /// the pre-check and the connection (DNS rebinding) cannot reach a private service. Connections to a configured proxy are
    /// allowed as-is; the proxy resolves the target itself.
    /// </summary>
    public static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endpoint = context.DnsEndPoint;
        var target = context.InitialRequestMessage.RequestUri;
        var direct = target is null || string.Equals(target.IdnHost, endpoint.Host, StringComparison.OrdinalIgnoreCase);
        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken).ConfigureAwait(false);
        if (direct && addresses.FirstOrDefault(a => !IsPublicAddress(a)) is { } blocked)
        {
            throw new HttpRequestException($"Connection to {endpoint.Host} was blocked because it resolves to the non-public address {blocked}.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task EnsurePublicAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme is not ("http" or "https")) Block(url, "unsupported scheme", "Only http/https URLs are supported.");
        var host = url.DnsSafeHost;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var literal) && !IsPublicAddress(literal)))
        {
            Block(url, "local or private host", "Local or private network addresses are not allowed.");
        }

        if (url.HostNameType != UriHostNameType.Dns) return;
        IPAddress[] addresses;
        try
        {
            addresses = await resolve(url.IdnHost, cancellationToken);
        }
        catch (SocketException ex)
        {
            // Not resolvable locally (e.g. only via a corporate proxy); the request itself will succeed or fail on its own.
            logger?.LogDebug("Job import: {Host} could not be resolved locally ({Error}); continuing", url.Host, ex.SocketErrorCode);
            return;
        }

        if (addresses.FirstOrDefault(a => !IsPublicAddress(a)) is { } blocked)
        {
            Block(url, $"{url.Host} resolves to non-public address {blocked}", "The address resolves to a local or private network address, which is not allowed.");
        }
    }

    [DoesNotReturn]
    private void Block(Uri url, string reason, string message)
    {
        logger?.LogWarning("Job import blocked for {Url}: {Reason}", url, reason);
        throw new ArgumentException(message);
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

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
