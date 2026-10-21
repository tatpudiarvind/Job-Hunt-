using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class CountryProfileTests
{
    [Theory]
    [InlineData("London, UK (Hybrid)", "GB")]
    [InlineData("Berlin, Germany", "DE")]
    [InlineData("München", "DE")]
    [InlineData("Dubai - UAE", "AE")]
    [InlineData("Remote - Singapore", "SG")]
    [InlineData("Toronto, ON", "CA")]
    [InlineData("Bengaluru, Karnataka", "IN")]
    [InlineData("Sydney NSW", "AU")]
    [InlineData("Dublin 2", "IE")]
    [InlineData("Amsterdam", "NL")]
    [InlineData("New York, NY", "US")]
    public void Detect_FindsCountryFromLocation(string location, string expected) =>
        Assert.Equal(expected, CountryProfiles.Detect(location));

    [Theory]
    [InlineData("Remote")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Duke Street")] // "uk" inside a word must not match
    public void Detect_ReturnsNullWhenUnknown(string? location) =>
        Assert.Null(CountryProfiles.Detect(location));

    [Fact]
    public void Get_UnknownCode_ReturnsDefault() =>
        Assert.Same(CountryProfiles.Default, CountryProfiles.Get("ZZ"));

    [Theory]
    [InlineData("UK", "GB")]
    [InlineData("gb", "GB")]
    [InlineData("Germany", "DE")]
    [InlineData("United Arab Emirates", "AE")]
    public void Normalize_HandlesAliasesAndNames(string value, string expected) =>
        Assert.Equal(expected, CountryProfiles.Normalize(value));

    [Fact]
    public void Profiles_HaveExpectedConventions()
    {
        Assert.True(CountryProfiles.Get("DE").PhotoExpected);
        Assert.False(CountryProfiles.Get("US").PhotoExpected);
        Assert.Equal("CV", CountryProfiles.Get("GB").DocumentName);
        Assert.Equal(SpellingVariant.UK, CountryProfiles.Get("AU").Spelling);
        Assert.Contains("Referees", CountryProfiles.Get("AU").RequiredSections);
        Assert.Equal(11, CountryProfiles.All.Count);
    }
}
