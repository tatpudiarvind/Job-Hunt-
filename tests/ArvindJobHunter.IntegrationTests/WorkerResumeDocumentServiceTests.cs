using System.Net;
using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain.Entities;
using ArvindJobHunter.Infrastructure.Resume;
using Xunit;

namespace ArvindJobHunter.IntegrationTests;

public sealed class WorkerResumeDocumentServiceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FallbackService : IResumeDocumentService
    {
        public int Reads { get; private set; }
        public Task<ResumeDocument> ReadAsync(string path, CancellationToken ct) { Reads++; return Task.FromResult(new ResumeDocument([])); }
        public Task<ResumeChangeReport> ApplyChangesAsync(string m, string o, IReadOnlyList<ResumeChange> c, CancellationToken ct) => throw new InvalidOperationException("must not be used");
    }

    private static WorkerResumeDocumentService Create(StubHandler handler, FallbackService fallback) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:5310") },
            new WorkerOptions { BaseUrl = "http://127.0.0.1:5310", SharedSecret = "s" },
            fallback);

    [Fact]
    public async Task ApplyChanges_PostsExpectedContract_AndParsesReport()
    {
        var report = new ResumeChangeReport(@"C:\out\abc.docx", [], []);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)), System.Text.Encoding.UTF8, "application/json")
        });
        var service = Create(handler, new FallbackService());

        var result = await service.ApplyChangesAsync(@"C:\master.docx", @"C:\out\abc.docx", [], CancellationToken.None);

        Assert.Equal(report.OutputPath, result.OutputPath);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/resume/apply", request.RequestUri!.AbsolutePath);
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
        Assert.Equal("abc", body.GetProperty("idempotencyKey").GetString());
        Assert.Equal("abc.docx", body.GetProperty("outputFileName").GetString());
    }

    [Fact]
    public async Task ApplyChanges_WorkerError_Throws_NoFallback()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("conflict") });
        var service = Create(handler, new FallbackService());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyChangesAsync("m", "o.docx", [], CancellationToken.None));
    }

    [Fact]
    public async Task Read_WorkerUnreachable_FallsBackInProcess()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("down"));
        var fallback = new FallbackService();
        var service = Create(handler, fallback);
        await service.ReadAsync("m.docx", CancellationToken.None);
        Assert.Equal(1, fallback.Reads);
    }

    [Fact]
    public void WorkerOptions_DisabledWithoutUrlAndSecret()
    {
        Assert.False(new WorkerOptions().Enabled);
        Assert.False(new WorkerOptions { BaseUrl = "http://127.0.0.1:5310" }.Enabled);
        Assert.True(new WorkerOptions { BaseUrl = "http://127.0.0.1:5310", SharedSecret = "x" }.Enabled);
    }
}
