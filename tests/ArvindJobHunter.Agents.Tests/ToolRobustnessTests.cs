using ArvindJobHunter.Agents.Tools;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Agents.Tests;

public sealed class ToolRobustnessTests
{
    private sealed class StubLlm(string content) : ILlmProvider
    {
        public string Name => "Stub";
        public bool IsConfigured => true;
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => Task.FromResult(new LlmResponse(content, Name, "stub"));
    }

    private sealed class ThrowingLlm(Exception error) : ILlmProvider
    {
        public string Name => "Throwing";
        public bool IsConfigured => true;
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) => Task.FromException<LlmResponse>(error);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static ToolContext Context(ILlmProvider llm) =>
        new(LocalUser.Id, ExecutionMode.DEMO, llm, AgentRun.Start("Test", "Test", null, ExecutionMode.DEMO, LocalUser.Id, llm.Name));

    private static Job AnalyzedJob() => Job.Create("Engineer", "Contoso", "", "MANUAL", null, "C# role")
        .WithAnalysis(new JobAnalysis(["C#"], [], "Senior", "s", [], DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("""{"score": 85.6, "matchedSkills": ["C#"], "missingSkills": [], "reason": "ok"}""", 86)]
    [InlineData("""{"score": "72", "matchedSkills": [], "missingSkills": ["C#"], "reason": "string score"}""", 72)]
    [InlineData("""Here you go: {"score": 140, "matchedSkills": [], "missingSkills": [], "reason": "clamped"}""", 100)]
    public async Task MatchJobTool_AcceptsDecimalAndStringScores(string llmOutput, int expected)
    {
        var job = AnalyzedJob();
        var facts = new[] { CandidateFact.Create("SKILL", "C#", "C#", "test").SetVerification(true) };

        var result = await new MatchJobTool().ExecuteAsync(new MatchJobInput(job, job.Analysis!, facts), Context(new StubLlm(llmOutput)), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(expected, result.Value!.Score);
    }

    [Fact]
    public async Task GenerateCoverLetterTool_FailsCleanly_WhenTheLlmOmitsTheBody()
    {
        var job = AnalyzedJob().WithMatch(new JobMatch(80, ["C#"], [], "r", DateTimeOffset.UtcNow), 60);

        var result = await new GenerateCoverLetterTool().ExecuteAsync(new GenerateEmailInput(job, job.Match!, "Arvind", "COVER_LETTER", null),
            Context(new StubLlm("""{"subject": "Hello"}""")), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("subject and a body", result.Error);
    }

    [Fact]
    public async Task ToolInvoker_RecordsAndLogsFailures_WithTheException()
    {
        var logger = new CapturingLogger();
        var failure = new HttpRequestException("LLM returned 401 Unauthorized: Incorrect API key provided");
        var context = Context(new ThrowingLlm(failure));

        var (result, run) = await ToolInvoker.InvokeAsync(new AnalyzeJobTool(), new AnalyzeJobInput(AnalyzedJob()), context, CancellationToken.None, logger);

        Assert.False(result.Succeeded);
        var call = Assert.Single(run.ToolCalls);
        Assert.False(call.Succeeded);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Tool AnalyzeJobTool failed", entry.Message);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public async Task ToolInvoker_TreatsTimeouts_AsToolFailures()
    {
        var context = Context(new ThrowingLlm(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.")));

        var (result, run) = await ToolInvoker.InvokeAsync(new AnalyzeJobTool(), new AnalyzeJobInput(AnalyzedJob()), context, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Timeout", result.Error);
        Assert.Single(run.ToolCalls);
    }
}
