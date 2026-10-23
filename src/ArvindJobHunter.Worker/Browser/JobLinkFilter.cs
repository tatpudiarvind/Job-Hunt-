using System.Text.RegularExpressions;

namespace ArvindJobHunter.Worker.Browser;

public sealed record RawLink(string Text, string Href);

/// <summary>
/// Picks the links on a search-results page that look like individual job postings, so the user can choose one
/// to preview. Pure filtering of what is already on the page; nothing is fetched or followed.
/// </summary>
public static partial class JobLinkFilter
{
    public const int MaxLinks = 100;

    public static IReadOnlyList<(string Text, string Url)> Filter(string pageUrl, IEnumerable<RawLink> links)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var page)) return [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, string)>();
        foreach (var link in links)
        {
            if (!Uri.TryCreate(page, link.Href, out var uri) || uri.Scheme is not ("http" or "https")) continue;
            var text = Whitespace().Replace(link.Text ?? "", " ").Trim();
            if (text.Length < 3 || text.Length > 200) continue;
            if (!JobPath().IsMatch(uri.AbsolutePath + uri.Query)) continue;
            if (Noise().IsMatch(text)) continue;
            var url = new UriBuilder(uri) { Fragment = "" }.Uri.ToString();
            if (url.Equals(page.ToString(), StringComparison.OrdinalIgnoreCase) || !seen.Add(url)) continue;
            result.Add((text, url));
            if (result.Count >= MaxLinks) break;
        }
        return result;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"/(jobs?|careers?|positions?|vacanc(y|ies)|openings?|stellen|viewjob|rc/clk)(/|\b)|[?&](jk|jobid|job_id|gh_jid|currentJobId)=", RegexOptions.IgnoreCase)]
    private static partial Regex JobPath();

    [GeneratedRegex(@"^(sign ?in|log ?in|register|next|previous|page \d+|\d+|search|filters?|save|share|apply now|see all jobs|all jobs|jobs|careers)$", RegexOptions.IgnoreCase)]
    private static partial Regex Noise();
}
