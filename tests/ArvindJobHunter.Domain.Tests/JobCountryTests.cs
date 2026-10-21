using ArvindJobHunter.Domain.Entities;

namespace ArvindJobHunter.Domain.Tests;

public sealed class JobCountryTests
{
    [Fact]
    public void Create_DetectsCountryFromLocation() =>
        Assert.Equal("DE", Job.Create("Engineer", "Contoso", "Berlin, Germany", "MANUAL", null, "Build APIs").CountryCode);

    [Fact]
    public void ResolveCountryCode_FallsBackToDescription()
    {
        var job = Job.Create("Engineer", "Contoso", "Remote", "MANUAL", null, "This role is based in our Dublin office.");
        Assert.Null(job.CountryCode);
        Assert.Equal("IE", job.ResolveCountryCode());
    }

    [Fact]
    public void WithCountry_NormalizesAndClears()
    {
        var job = Job.Create("Engineer", "Contoso", "Remote", "MANUAL", null, "Build APIs");
        Assert.Equal("GB", job.WithCountry("UK").CountryCode);
        Assert.Null(job.WithCountry("UK").WithCountry(null).CountryCode);
    }
}
