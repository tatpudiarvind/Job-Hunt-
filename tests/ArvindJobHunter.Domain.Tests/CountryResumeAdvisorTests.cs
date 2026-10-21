using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class CountryResumeAdvisorTests
{
    private static readonly string[] Complete = ["Summary", "Engineer who builds APIs.", "Experience", "Contoso 2020-2024", "Skills", "C#", "Education", "BSc"];

    [Fact]
    public void Check_PassesCompleteUsResume() =>
        Assert.Empty(CountryResumeAdvisor.Check(Complete, CountryProfiles.Get("US")));

    [Fact]
    public void Check_FlagsMissingReferees_ForAustralia() =>
        Assert.Contains(CountryResumeAdvisor.Check(Complete, CountryProfiles.Get("AU")), i => i.Contains("Referees"));

    [Fact]
    public void Check_FlagsUsSpelling_ForUk()
    {
        var issues = CountryResumeAdvisor.Check([.. Complete, "Optimized the platform."], CountryProfiles.Get("GB"));
        Assert.Contains(issues, i => i.Contains("UK spelling"));
    }

    [Fact]
    public void Check_FlagsDateOfBirth_WhereNotExpected() =>
        Assert.Contains(CountryResumeAdvisor.Check([.. Complete, "Date of birth: 1 Jan 1990"], CountryProfiles.Get("GB")), i => i.Contains("date of birth"));

    [Fact]
    public void Check_FlagsTooManyPages()
    {
        var long_ = Complete.Concat(Enumerable.Repeat(new string('x', 1000), 10)).ToList();
        Assert.Contains(CountryResumeAdvisor.Check(long_, CountryProfiles.Get("US")), i => i.Contains("pages"));
    }

    [Theory]
    [InlineData("Optimized the Organization", SpellingVariant.UK, "Optimised the Organisation")]
    [InlineData("ANALYZED", SpellingVariant.UK, "ANALYSED")]
    [InlineData("Analysed behaviour", SpellingVariant.US, "Analyzed behavior")]
    [InlineData("Size optimizer", SpellingVariant.UK, "Size optimizer")]
    public void ToSpelling_ConvertsKeepingCase(string input, SpellingVariant target, string expected) =>
        Assert.Equal(expected, CountryResumeAdvisor.ToSpelling(input, target));
}
