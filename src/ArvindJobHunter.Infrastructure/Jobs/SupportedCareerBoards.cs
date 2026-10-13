namespace ArvindJobHunter.Infrastructure.Jobs;

internal static class SupportedCareerBoards
{
    internal sealed record GreenhouseBoard(string Company, string BoardToken);
    internal sealed record LeverBoard(string Company, string AccountToken);

    // Curated public ATS boards that commonly advertise software roles in the UK/EU.
    // This list is intentionally small for the first phase and can be expanded safely over time.
    public static readonly IReadOnlyList<GreenhouseBoard> Greenhouse =
    [
        new("Monzo", "monzo"),
        new("Cleo", "cleo"),
        new("Mollie", "mollie")
    ];

    public static readonly IReadOnlyList<LeverBoard> Lever =
    [
        new("Wise", "wise"),
        new("Form3", "form3"),
        new("Remote", "remotecom")
    ];
}
