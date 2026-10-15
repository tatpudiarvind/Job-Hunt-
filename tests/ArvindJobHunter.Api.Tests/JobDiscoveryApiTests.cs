using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArvindJobHunter.Api.Tests;

public sealed class JobDiscoveryApiTests
{
    [Fact]
    public async Task DiscoverEndpoint_ReturnsOnlyImportedUkEuSponsoredJobs()
    {
        using var factory = new DiscoveryApiFactory();
        var client = await SignedInClientAsync(factory);
        foreach (var skill in new[] { "C#", "Azure" })
        {
            var createdFact = await client.PostAsJsonAsync("/api/candidate-profile/facts", new { factType = "SKILL", name = skill, value = skill, sourceReference = "Test setup" });
            createdFact.EnsureSuccessStatusCode();
            var fact = await createdFact.Content.ReadFromJsonAsync<JsonElement>();
            (await client.PostAsync($"/api/candidate-profile/facts/{fact.GetProperty("id").GetGuid()}/verify", null)).EnsureSuccessStatusCode();
        }

        var response = await client.PostAsJsonAsync("/api/jobs/discover", new { maxResults = 10 });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("imported").GetInt32());
        Assert.Equal(1, body.GetProperty("jobs").GetArrayLength());
        var job = body.GetProperty("jobs")[0];
        Assert.Equal("Visa Sponsored .NET Engineer", job.GetProperty("title").GetString());
        Assert.Contains("Germany", job.GetProperty("location").GetString());
    }

    private static async Task<HttpClient> SignedInClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/status");
        var credentials = new { username = "arvind", password = "correct-horse-battery-staple" };
        if (!status.GetProperty("configured").GetBoolean())
        {
            (await client.PostAsJsonAsync("/api/auth/setup", credentials)).EnsureSuccessStatusCode();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return client;
    }

    private sealed class DiscoveryApiFactory : WebApplicationFactory<Program>
    {
        private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), $"ajh-discovery-api-tests-{Guid.NewGuid():N}");

        public DiscoveryApiFactory()
        {
            Environment.SetEnvironmentVariable("Storage__DataDirectory", dataDirectory);
            Environment.SetEnvironmentVariable("Runtime__Mode", "DEMO");
            Environment.SetEnvironmentVariable("Runtime__LlmProvider", "Demo");
            _ = Server;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IJobDiscoverySource>();
                services.AddSingleton<IJobDiscoverySource>(new FakeSource(
                [
                    new DiscoveredJobPosting("1", "Visa Sponsored .NET Engineer", "Contoso GmbH", "Berlin, Germany", "Fake", "https://example.com/jobs/1", "C#, Azure, visa sponsorship available, remote in Europe.", []),
                    new DiscoveredJobPosting("2", "No Sponsorship Engineer", "Fabrikam", "Paris, France", "Fake", "https://example.com/jobs/2", "C#, Azure, no visa sponsorship.", [])
                ]));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("Storage__DataDirectory", null);
            Environment.SetEnvironmentVariable("Runtime__Mode", null);
            Environment.SetEnvironmentVariable("Runtime__LlmProvider", null);
            if (Directory.Exists(dataDirectory)) { try { Directory.Delete(dataDirectory, true); } catch { } }
        }
    }

    private sealed class FakeSource(IReadOnlyList<DiscoveredJobPosting> jobs) : IJobDiscoverySource
    {
        public string Name => "Fake";
        public Task<IReadOnlyList<DiscoveredJobPosting>> SearchAsync(JobDiscoveryRequest request, CancellationToken cancellationToken) => Task.FromResult(jobs);
    }
}
