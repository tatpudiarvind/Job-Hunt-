using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class PreferredCountryBoostTests
{
    private static readonly JobMatch Match = new(70, [], [], "Good fit", DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("GB", new[] { "United Kingdom" }, 75)]
    [InlineData("DE", new[] { "Germany", "UK" }, 75)]
    [InlineData("US", new[] { "Germany" }, 70)]
    [InlineData(null, new[] { "Germany" }, 70)]
    public void Boost_AppliesOnlyToPreferredCountries(string? country, string[] preferred, int expected) =>
        Assert.Equal(expected, CountryProfiles.BoostForPreferredCountry(Match, country, preferred).Score);

    [Fact]
    public void Boost_CapsAt100() =>
        Assert.Equal(100, CountryProfiles.BoostForPreferredCountry(Match with { Score = 98 }, "SG", ["SG"]).Score);
}
