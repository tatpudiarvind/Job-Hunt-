namespace ArvindJobHunter.Infrastructure.Jobs;

internal static class SupportedCareerBoards
{
    internal sealed record GreenhouseBoard(string Company, string BoardToken);
    internal sealed record LeverBoard(string Company, string AccountToken);

    // Curated public ATS boards of companies that hire software engineers across the UK and Europe.
    // Every token below was verified against the public board API; extend the list as needed.
    public static readonly IReadOnlyList<GreenhouseBoard> Greenhouse =
    [
        new("Monzo", "monzo"),
        new("Cleo", "cleo"),
        new("Mollie", "mollie"),
        new("N26", "n26"),
        new("GetYourGuide", "getyourguide"),
        new("Celonis", "celonis"),
        new("Contentful", "contentful"),
        new("HelloFresh", "hellofresh"),
        new("Adyen", "adyen"),
        new("Elastic", "elastic"),
        new("Datadog", "datadog"),
        new("GitLab", "gitlab"),
        new("trivago", "trivago"),
        new("SumUp", "sumup"),
        new("Wolt", "wolt"),
        new("JetBrains", "jetbrains"),
        new("Solaris", "solarisbank"),
        new("Raisin", "raisin"),
        new("Flix", "flix"),
        new("Doctolib", "doctolib"),
        new("Mirakl", "mirakl"),
        new("Grafana Labs", "grafanalabs"),
        new("MongoDB", "mongodb"),
        new("Cloudflare", "cloudflare"),
        new("Twilio", "twilio"),
        new("Canonical", "canonical")
    ];

    public static readonly IReadOnlyList<LeverBoard> Lever =
    [
        new("Wise", "wise"),
        new("Form3", "form3"),
        new("Remote", "remotecom"),
        new("Spotify", "spotify"),
        new("Pipedrive", "pipedrive"),
        new("Swile", "swile")
    ];
}
