using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Infrastructure.Persistence;

namespace ArvindJobHunter.IntegrationTests;

public sealed class JsonJobSearchPreferencesProviderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-job-search-prefs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public async Task SaveAndLoadAsync_NormalizesListsAndOptionalValues()
    {
        var provider = new JsonJobSearchPreferencesProvider(new JsonFileStore<JobSearchPreferences>(directory, "job-search-preferences.json"));

        await provider.SaveAsync(new JobSearchPreferences
        {
            PreferredCountries = [" Germany ", "Germany", "Netherlands"],
            LinkedInProfileUrl = " https://www.linkedin.com/in/arvind-tatpudi-071137120/ ",
            ResumeLinkedInProfileUrl = " ",
            PreferredRoles = [" Backend Engineer ", "Backend Engineer", "Platform Engineer"],
            WorkMode = " Hybrid ",
            SalaryExpectation = " €85,000 ",
            WorkAuthorization = " Needs sponsorship "
        }, CancellationToken.None);

        var loaded = await provider.GetAsync(CancellationToken.None);

        Assert.Equal(["Germany", "Netherlands"], loaded.PreferredCountries);
        Assert.Equal("https://www.linkedin.com/in/arvind-tatpudi-071137120/", loaded.LinkedInProfileUrl);
        Assert.Null(loaded.ResumeLinkedInProfileUrl);
        Assert.Equal(["Backend Engineer", "Platform Engineer"], loaded.PreferredRoles);
        Assert.Equal("Hybrid", loaded.WorkMode);
        Assert.Equal("€85,000", loaded.SalaryExpectation);
        Assert.Equal("Needs sponsorship", loaded.WorkAuthorization);
    }
}
