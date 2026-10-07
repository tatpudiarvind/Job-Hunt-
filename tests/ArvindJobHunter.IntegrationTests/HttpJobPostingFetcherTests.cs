using System.Net;
using ArvindJobHunter.Infrastructure.Jobs;
using Xunit;

namespace ArvindJobHunter.IntegrationTests;

public sealed class HttpJobPostingFetcherTests
{
    private sealed class StubHandler(string html, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") });
    }

    private static HttpJobPostingFetcher Create(string html, HttpStatusCode status = HttpStatusCode.OK) => new(new HttpClient(new StubHandler(html, status)));

    [Fact]
    public async Task Extracts_JsonLd_JobPosting()
    {
        const string html = """
            <html><head><title>ignored</title>
            <script type="application/ld+json">{"@context":"https://schema.org","@type":"JobPosting","title":"Senior .NET Engineer",
            "hiringOrganization":{"@type":"Organization","name":"Contoso"},
            "jobLocation":{"@type":"Place","address":{"addressLocality":"Pune","addressCountry":"IN"}},
            "description":"<p>Build <b>APIs</b> with ASP.NET Core.</p>"}</script></head><body>x</body></html>
            """;
        var draft = await Create(html).FetchAsync(new Uri("https://jobs.example.com/1"), CancellationToken.None);

        Assert.Equal("Senior .NET Engineer", draft.Title);
        Assert.Equal("Contoso", draft.Company);
        Assert.Equal("Pune, IN", draft.Location);
        Assert.Equal("Build APIs with ASP.NET Core.", draft.Description);
        Assert.Equal("jobs.example.com", draft.Source);
    }

    [Fact]
    public async Task FallsBack_To_Title_And_Body_With_Warning()
    {
        var html = "<html><head><title>Backend Dev - Fabrikam</title></head><body><nav>menu</nav><h1>Backend Dev</h1><p>" + new string('a', 300) + "</p><script>evil()</script></body></html>";
        var draft = await Create(html).FetchAsync(new Uri("https://example.com/j"), CancellationToken.None);

        Assert.Equal("Backend Dev - Fabrikam", draft.Title);
        Assert.DoesNotContain("evil", draft.Description);
        Assert.DoesNotContain("menu", draft.Description);
        Assert.Contains(draft.Warnings, w => w.Contains("Structured job data was not found"));
    }

    [Theory]
    [InlineData("http://localhost/x")]
    [InlineData("http://127.0.0.1:5228/api")]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://169.254.169.254/latest")]
    [InlineData("ftp://example.com/")]
    public async Task Rejects_Private_Or_NonHttp(string url)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Create("<html/>").FetchAsync(new Uri(url), CancellationToken.None));
    }

    [Fact]
    public async Task NonSuccess_Status_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create("", HttpStatusCode.Forbidden).FetchAsync(new Uri("https://example.com/"), CancellationToken.None));
    }
}
