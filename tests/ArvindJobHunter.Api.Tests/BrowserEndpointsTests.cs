using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Contracts.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArvindJobHunter.Api.Tests;

public sealed class BrowserEndpointsTests
{
    [Fact]
    public async Task ImportDownload_CopiesWorkerDownloadIntoLibrary()
    {
        using var factory = new BrowserApiFactory();
        var download = factory.AddDownload("Resume-UK.pdf");
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/browser/downloads/import", new { path = download, label = "UK resume", keywords = new[] { " uk ", "", ".net" } });

        response.EnsureSuccessStatusCode();
        var entry = await response.Content.ReadFromJsonAsync<JsonElement>();
        var stored = entry.GetProperty("path").GetString()!;
        Assert.StartsWith(Path.GetFullPath(Path.Combine(factory.DataDirectory, "resumes", "library")), Path.GetFullPath(stored));
        Assert.True(File.Exists(stored));
        Assert.Equal(await File.ReadAllBytesAsync(download), await File.ReadAllBytesAsync(stored));
        Assert.Equal("UK resume", entry.GetProperty("label").GetString());
        Assert.Equal(["uk", ".net"], entry.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()!).ToArray());

        var library = await client.GetFromJsonAsync<JsonElement>("/api/browser/resume-library");
        Assert.Contains(library.EnumerateArray(), e => e.GetProperty("path").GetString() == stored);
    }

    [Fact]
    public async Task ImportDownload_RejectsFilesTheBrowserDidNotDownload()
    {
        using var factory = new BrowserApiFactory();
        factory.AddDownload("Resume.pdf");
        var outsider = Path.Combine(factory.DataDirectory, "outside.pdf");
        Directory.CreateDirectory(factory.DataDirectory);
        await File.WriteAllTextAsync(outsider, "%PDF-1.4 not downloaded");
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/browser/downloads/import", new { path = outsider, label = (string?)null, keywords = (string[]?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(factory.DataDirectory, "resumes", "library")) &&
                     Directory.EnumerateFiles(Path.Combine(factory.DataDirectory, "resumes", "library")).Any());
        var library = await client.GetFromJsonAsync<JsonElement>("/api/browser/resume-library");
        Assert.Equal(0, library.GetArrayLength());
    }

    [Fact]
    public async Task ImportDownload_RejectsListedFileThatNoLongerExists()
    {
        using var factory = new BrowserApiFactory();
        var download = factory.AddDownload("Gone.pdf");
        File.Delete(download);
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/browser/downloads/import", new { path = download, label = (string?)null, keywords = (string[]?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CaptureJob_ReturnsDraftWithoutSavingAJob()
    {
        using var factory = new BrowserApiFactory();
        factory.Browser.Page = new CapturedJobPage("https://careers.contoso.com/jobs/42", "Senior .NET Engineer", "Senior .NET Engineer | Contoso", "We sponsor visas. C#, Azure.");
        var client = await SignedInClientAsync(factory);
        var jobsBefore = (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength();

        var response = await client.PostAsync("/api/browser/capture-job", null);

        response.EnsureSuccessStatusCode();
        var draft = await response.Content.ReadFromJsonAsync<JobPostingDraft>();
        Assert.NotNull(draft);
        Assert.Equal("https://careers.contoso.com/jobs/42", draft!.Url);
        Assert.Equal("Senior .NET Engineer", draft.Title);
        Assert.Equal("Contoso", draft.Company);
        Assert.Contains("sponsor", draft.Description);
        Assert.Equal(jobsBefore, (await client.GetFromJsonAsync<JsonElement>("/api/jobs")).GetArrayLength());
    }

    [Fact]
    public async Task CaptureJob_WarnsWhenCompanyCannotBeRead()
    {
        using var factory = new BrowserApiFactory();
        factory.Browser.Page = new CapturedJobPage("https://example.com/job", "Engineer", "Engineer", "text");
        var client = await SignedInClientAsync(factory);

        var draft = await (await client.PostAsync("/api/browser/capture-job", null)).Content.ReadFromJsonAsync<JobPostingDraft>();

        Assert.Null(draft!.Company);
        Assert.NotEmpty(draft.Warnings);
    }

    [Fact]
    public async Task CaptureJob_WithNoOpenPage_Returns404()
    {
        using var factory = new BrowserApiFactory();
        var client = await SignedInClientAsync(factory);

        var response = await client.PostAsync("/api/browser/capture-job", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ImportDownload_AsCoverLetter_IsStoredWithKind()
    {
        using var factory = new BrowserApiFactory();
        var download = factory.AddDownload("Cover.pdf");
        var client = await SignedInClientAsync(factory);

        var entry = await (await client.PostAsJsonAsync("/api/browser/downloads/import", new { path = download, label = "Cover", keywords = new[] { ".net" }, kind = "COVER_LETTER" }))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("COVER_LETTER", entry.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task SavedSearches_RoundTripAndRejectInvalidUrls()
    {
        using var factory = new BrowserApiFactory();
        var client = await SignedInClientAsync(factory);

        var bad = await client.PutAsJsonAsync("/api/browser/searches", new { searches = new[] { new { name = "x", url = "javascript:alert(1)" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        (await client.PutAsJsonAsync("/api/browser/searches", new { searches = new[] { new { name = " .NET Berlin ", url = "https://jobs.example.com/search?q=.net&l=berlin" } } })).EnsureSuccessStatusCode();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/browser/searches");
        Assert.Equal(1, saved.GetArrayLength());
        Assert.Equal(".NET Berlin", saved[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Links_ReturnsWorkerLinksOr404()
    {
        using var factory = new BrowserApiFactory();
        var client = await SignedInClientAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/browser/links", null)).StatusCode);

        factory.Browser.Links = new PageLinks("https://jobs.example.com/search", [new PageLink("Engineer", "https://jobs.example.com/jobs/1")]);
        var page = await (await client.PostAsync("/api/browser/links", null)).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://jobs.example.com/jobs/1", page.GetProperty("links")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task BrowserEndpoints_RequireSignIn()
    {
        using var factory = new BrowserApiFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/browser/capture-job", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/browser/downloads/import", new { path = "x" })).StatusCode);
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

    private sealed class BrowserApiFactory : WebApplicationFactory<Program>
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), $"ajh-browser-api-tests-{Guid.NewGuid():N}");
        private string DownloadDirectory => Path.Combine(DataDirectory + "-downloads");
        public FakeBrowser Browser { get; } = new();

        public BrowserApiFactory()
        {
            Environment.SetEnvironmentVariable("Storage__DataDirectory", DataDirectory);
            Environment.SetEnvironmentVariable("Runtime__Mode", "DEMO");
            Environment.SetEnvironmentVariable("Runtime__LlmProvider", "Demo");
            _ = Server;
        }

        public string AddDownload(string name)
        {
            Directory.CreateDirectory(DownloadDirectory);
            var path = Path.Combine(DownloadDirectory, name);
            File.WriteAllText(path, $"%PDF-1.4 {name}");
            Browser.Downloads.Add(new DownloadedFile(path, name, new FileInfo(path).Length, DateTimeOffset.UtcNow));
            return path;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IBrowserApplyClient>();
                services.AddSingleton<IBrowserApplyClient>(Browser);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("Storage__DataDirectory", null);
            Environment.SetEnvironmentVariable("Runtime__Mode", null);
            Environment.SetEnvironmentVariable("Runtime__LlmProvider", null);
            foreach (var directory in new[] { DataDirectory, DownloadDirectory })
            {
                if (Directory.Exists(directory)) { try { Directory.Delete(directory, true); } catch { } }
            }
        }
    }

    private sealed class FakeBrowser : IBrowserApplyClient
    {
        public List<DownloadedFile> Downloads { get; } = [];
        public CapturedJobPage? Page { get; set; }

        public Task<IReadOnlyList<DownloadedFile>> DownloadsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DownloadedFile>>(Downloads);
        public Task<CapturedJobPage?> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(Page);
        public PageLinks? Links { get; set; }
        public Task<PageLinks?> LinksAsync(CancellationToken cancellationToken) => Task.FromResult(Links);
        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<FillPreviewResponse> PrepareAsync(FillPreviewRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task BindAsync(string sessionId, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SubmitResponse> SubmitAsync(SubmitRequest request, CancellationToken cancellationToken) => throw new NotSupportedException("Tests must never submit.");
    }
}
