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

    private sealed class RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    // Hermetic DNS: every host resolves to a public documentation address unless a test says otherwise.
    private static readonly HostAddressResolver PublicDns = (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });

    private static HttpJobPostingFetcher Create(string html, HttpStatusCode status = HttpStatusCode.OK) => new(new HttpClient(new StubHandler(html, status)), null, PublicDns);

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = location.StartsWith('/') ? new Uri(location, UriKind.Relative) : new Uri(location) } };

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

    [Fact]
    public async Task Redirect_ToAPrivateAddress_IsBlocked_BeforeItIsRequested()
    {
        var handler = new RoutingHandler(_ => Redirect("http://127.0.0.1:5228/api/auth/status"));
        var fetcher = new HttpJobPostingFetcher(new HttpClient(handler), null, PublicDns);

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchAsync(new Uri("https://jobs.example.com/1"), CancellationToken.None));
        Assert.Equal([new Uri("https://jobs.example.com/1")], handler.Requested);
    }

    [Fact]
    public async Task Redirect_ToAPublicPage_IsFollowed()
    {
        var handler = new RoutingHandler(request => request.RequestUri!.AbsolutePath == "/1"
            ? Redirect("/careers/42")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html><head><title>Platform Engineer</title></head><body>" + new string('a', 300) + "</body></html>") });
        var fetcher = new HttpJobPostingFetcher(new HttpClient(handler), null, PublicDns);

        var draft = await fetcher.FetchAsync(new Uri("https://jobs.example.com/1"), CancellationToken.None);

        Assert.Equal("Platform Engineer", draft.Title);
        Assert.Equal([new Uri("https://jobs.example.com/1"), new Uri("https://jobs.example.com/careers/42")], handler.Requested);
    }

    [Fact]
    public async Task TooManyRedirects_Fail()
    {
        var handler = new RoutingHandler(request => Redirect(request.RequestUri!.AbsoluteUri + "x"));
        var fetcher = new HttpJobPostingFetcher(new HttpClient(handler), null, PublicDns);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.FetchAsync(new Uri("https://jobs.example.com/a"), CancellationToken.None));
        Assert.Equal(6, handler.Requested.Count);
    }

    [Fact]
    public async Task HostResolvingToAPrivateAddress_IsBlocked()
    {
        var handler = new RoutingHandler(_ => throw new InvalidOperationException("must not be requested"));
        HostAddressResolver internalDns = (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.0.0.5") });
        var fetcher = new HttpJobPostingFetcher(new HttpClient(handler), null, internalDns);

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.FetchAsync(new Uri("https://intranet.example.com/jobs"), CancellationToken.None));
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public async Task ConnectCallback_RefusesToDialAPrivateAddress_EvenIfThePreCheckWasBypassed()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, ConnectCallback = HttpJobPostingFetcher.ConnectToPublicAddressAsync });

            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://localhost:{port}/"));

            Assert.Contains("non-public address", error.ToString());
            Assert.False(listener.Pending());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    public void IsPublicAddress_ClassifiesAddresses(string address, bool expected) =>
        Assert.Equal(expected, HttpJobPostingFetcher.IsPublicAddress(IPAddress.Parse(address)));
}
