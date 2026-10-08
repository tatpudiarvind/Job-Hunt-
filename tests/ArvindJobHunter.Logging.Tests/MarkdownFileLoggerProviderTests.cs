using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Logging.Tests;

public sealed class MarkdownFileLoggerProviderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), $"ajh-mdlog-{Guid.NewGuid():N}");
    private readonly ManualClock clock = new(new DateTimeOffset(2026, 10, 8, 9, 30, 0, TimeSpan.FromHours(5.5)));

    public void Dispose()
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { /* best effort */ }
    }

    private MarkdownFileLoggerProvider CreateProvider(Action<MarkdownFileLoggerOptions>? configure = null)
    {
        var options = new MarkdownFileLoggerOptions { Directory = directory, FileNamePrefix = "test", Title = "Test Host" };
        configure?.Invoke(options);
        return new MarkdownFileLoggerProvider(options, new TestEnvironment(directory), clock);
    }

    private string[] LogFiles() => Directory.GetFiles(directory, "*.md").OrderBy(f => f, StringComparer.Ordinal).ToArray();

    private string[] LogFileNames() => LogFiles().Select(f => Path.GetFileName(f)).ToArray();

    private string ReadSingleLog() => File.ReadAllText(Assert.Single(LogFiles()));

    private static int Count(string text, string value) => Regex.Matches(text, Regex.Escape(value)).Count;

    [Fact]
    public void WritesHeader_SessionMetadata_TableRows_AndFooter()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("ArvindJobHunter.Application.Features.ExecuteApprovedActionCommand");
            logger.LogInformation("Executed approval {ApprovalId}", 42);
            logger.LogWarning("Approval {ApprovalId} is about to expire", 7);
        }

        Assert.Equal("test-2026-10-08.md", Path.GetFileName(Assert.Single(LogFiles())));
        var log = ReadSingleLog();
        Assert.StartsWith("# 📒 Test Host — log for Thursday, 8 October 2026", log);
        Assert.Contains("local time (UTC+05:30)", log);
        Assert.Contains("## ▶️ Session started · 09:30:00 · PID", log);
        Assert.Contains("| Environment | Test |", log);
        Assert.Contains(MarkdownLogFormatter.TableHeader, log);
        Assert.Contains("| 09:30:00.000 | ℹ️ INFO | Features.ExecuteApprovedActionCommand | Executed approval 42 |", log);
        Assert.Contains("| ⚠️ WARN | Features.ExecuteApprovedActionCommand | Approval 7 is about to expire |", log);
        Assert.Contains("### ⏹️ Session ended", log);
        Assert.Contains("entries: 2 · warnings: 1 · errors: 0", log);
    }

    [Fact]
    public void Exceptions_AreWrittenToCollapsibleBlocks_AndTheTableRestarts()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Agents.AgentOrchestrator");
            logger.LogInformation("Before");
            logger.LogError(new InvalidOperationException("Boom <b>"), "Agent run failed");
            logger.LogInformation("After");
        }

        var log = ReadSingleLog();
        Assert.Contains("| ❌ **ERROR** | Agents.AgentOrchestrator | Agent run failed ⤵ |", log);
        Assert.Contains("<details>\n<summary>❌ 09:30:00.000 · <code>System.InvalidOperationException: Boom &lt;b&gt;</code></summary>", log);
        Assert.Contains("```text\nSystem.InvalidOperationException: Boom <b>\n```", log);
        Assert.Equal(2, Count(log, MarkdownLogFormatter.TableHeader));
        Assert.True(log.IndexOf("After", StringComparison.Ordinal) > log.IndexOf("</details>", StringComparison.Ordinal));
        Assert.Contains("errors: 1", log);
    }

    [Fact]
    public void Secrets_AreMasked_BeforeReachingTheFile()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Security");
            logger.LogInformation("Authorization: Bearer abc.DEF-123_token");
            logger.LogInformation("OpenAI key {Key} and {Payload}", "sk-proj-ABCDEFGHIJKLMNOP", "{\"llmApiKey\":\"my-secret-value\"}");
            logger.LogInformation("GET /api/integrations/google/callback?code=4/0AbCdEf&state=STATE123 → 302");
            logger.LogInformation("password=hunter2; Model=gpt-4o-mini");
            logger.LogError(new InvalidOperationException("token refresh failed: refresh_token=1//0gSecret"), "Google token refresh failed");
        }

        var log = ReadSingleLog();
        foreach (var secret in new[] { "abc.DEF-123_token", "ABCDEFGHIJKLMNOP", "my-secret-value", "4/0AbCdEf", "STATE123", "hunter2", "1//0gSecret" })
        {
            Assert.DoesNotContain(secret, log);
        }

        Assert.Contains("Bearer ***", log);
        Assert.Contains("callback?code=***&state=*** → 302", log);
        Assert.Contains("Model=gpt-4o-mini", log);
    }

    [Fact]
    public void Redaction_CanBeTurnedOff()
    {
        using (var provider = CreateProvider(o => o.RedactSecrets = false))
        {
            provider.CreateLogger("Diagnostics").LogInformation("password=plain-text");
        }

        Assert.Contains("password=plain-text", ReadSingleLog());
    }

    [Fact]
    public void TableBreakingCharacters_AreEscaped()
    {
        using (var provider = CreateProvider())
        {
            provider.CreateLogger("Escaping").LogInformation("a | b <script>alert(1)</script>\nsecond line");
        }

        Assert.Contains("| a \\| b &lt;script&gt;alert(1)&lt;/script&gt;<br>second line |", ReadSingleLog());
    }

    [Fact]
    public void LongMessages_AreTruncated()
    {
        using (var provider = CreateProvider(o => o.MaxMessageLength = 50))
        {
            provider.CreateLogger("Big").LogInformation(new string('x', 500));
        }

        var log = ReadSingleLog();
        Assert.Contains(new string('x', 50) + "… [truncated 450 characters]", log);
        Assert.DoesNotContain(new string('x', 51), log);
    }

    [Fact]
    public void RollsOverAtMidnight_AndMarksTheSessionAsContinued()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Clock");
            logger.LogInformation("Before midnight");
            clock.Advance(TimeSpan.FromHours(15));
            logger.LogInformation("After midnight");
        }

        var files = LogFiles();
        Assert.Equal(["test-2026-10-08.md", "test-2026-10-09.md"], LogFileNames());
        var first = File.ReadAllText(files[0]);
        var second = File.ReadAllText(files[1]);
        Assert.Contains("Before midnight", first);
        Assert.Contains("_The session continues in `test-2026-10-09.md`._", first);
        Assert.DoesNotContain("Session ended", first);
        Assert.StartsWith("# 📒 Test Host — log for Friday, 9 October 2026", second);
        Assert.Contains("## ⏩ Session continued · 00:30:00", second);
        Assert.Contains("_Continued from `test-2026-10-08.md`", second);
        Assert.Contains("After midnight", second);
        Assert.Contains("Session ended", second);
    }

    [Fact]
    public void RollsOverToPartFiles_WhenTheSizeLimitIsReached()
    {
        using (var provider = CreateProvider(o => o.MaxFileSizeBytes = 4096))
        {
            var logger = provider.CreateLogger("Volume");
            for (var i = 0; i < 80; i++) logger.LogInformation("Entry {Index:000} {Padding}", i, new string('x', 100));
        }

        var files = LogFiles().OrderBy(f => f.Length).ThenBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 3, $"Expected several part files but found {files.Length}.");
        Assert.Equal("test-2026-10-08.md", Path.GetFileName(files[0]));
        Assert.Equal("test-2026-10-08-002.md", Path.GetFileName(files[1]));
        var all = string.Concat(files.Select(File.ReadAllText));
        for (var i = 0; i < 80; i++) Assert.Contains($"Entry {i:000} ", all);
        Assert.All(files[..^1], f => Assert.True(new FileInfo(f).Length < 4096 + 1024));
    }

    [Fact]
    public void Retention_DeletesOnlyTheOldestFilesOfThisLogger()
    {
        Directory.CreateDirectory(directory);
        for (var day = 1; day <= 10; day++) File.WriteAllText(Path.Combine(directory, $"test-2026-09-{day:00}.md"), "old");
        File.WriteAllText(Path.Combine(directory, "other-2026-09-01.md"), "other logger");
        File.WriteAllText(Path.Combine(directory, "test-notes.md"), "not a log file");

        using (var provider = CreateProvider(o => o.RetainedFileCountLimit = 3))
        {
            provider.CreateLogger("Retention").LogInformation("hello");
        }

        Assert.Equal(["other-2026-09-01.md", "test-2026-09-09.md", "test-2026-09-10.md", "test-2026-10-08.md", "test-notes.md"], LogFileNames());
    }

    [Fact]
    public void AppendsANewSession_WhenTheFileAlreadyExists()
    {
        using (var first = CreateProvider()) first.CreateLogger("Run").LogInformation("first run");
        using (var second = CreateProvider()) second.CreateLogger("Run").LogInformation("second run");

        var log = ReadSingleLog();
        Assert.Equal(1, Count(log, "# 📒 Test Host"));
        Assert.Equal(2, Count(log, "## ▶️ Session started"));
        Assert.Equal(2, Count(log, "### ⏹️ Session ended"));
        Assert.True(log.IndexOf("first run", StringComparison.Ordinal) < log.IndexOf("second run", StringComparison.Ordinal));
    }

    [Fact]
    public void ConcurrentLogging_WritesEveryEntry()
    {
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Parallel");
            Parallel.For(0, 500, i => logger.LogInformation("Parallel entry {Index}", i));
        }

        var log = ReadSingleLog();
        Assert.Equal(500, Count(log, "| Parallel entry "));
        Assert.Contains("entries: 500", log);
    }

    [Fact]
    public void DisabledProvider_WritesNothing()
    {
        using (var provider = CreateProvider(o => o.Enabled = false))
        {
            var logger = provider.CreateLogger("Off");
            Assert.False(logger.IsEnabled(LogLevel.Critical));
            logger.LogCritical("should not be written");
        }

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void LoggingAfterDispose_IsIgnored()
    {
        var provider = CreateProvider();
        var logger = provider.CreateLogger("Late");
        provider.Dispose();
        provider.Dispose();

        logger.LogInformation("too late");
        Assert.DoesNotContain("too late", ReadSingleLog());
    }

    /// <summary>Polls with File.ReadAllText, which fails if anyone holds the file open for writing.</summary>
    private static string WaitForText(string path, string expected)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    if (text.Contains(expected, StringComparison.Ordinal)) return text;
                }
            }
            catch (IOException)
            {
                // the writer may be in the middle of a (sub-millisecond) append
            }

            Thread.Sleep(50);
        }

        throw new Xunit.Sdk.XunitException($"'{expected}' never appeared in {path}.");
    }

    [Fact]
    public void TheLogFile_IsNotKeptOpen_SoOrdinaryReadersCanOpenItWhileTheAppRuns()
    {
        using var provider = CreateProvider();
        provider.CreateLogger("Reader").LogInformation("visible while running");

        var text = WaitForText(Path.Combine(directory, "test-2026-10-08.md"), "visible while running");

        Assert.Contains("## ▶️ Session started", text);
    }

    [Fact]
    public void AWriteThatFailedWhileTheFileWasLocked_IsRetried_WithoutWaitingForAnotherEntry()
    {
        var path = Path.Combine(directory, "test-2026-10-08.md");
        using var provider = CreateProvider();
        var logger = provider.CreateLogger("Retry");
        logger.LogInformation("first entry");
        WaitForText(path, "first entry");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            logger.LogInformation("written while locked");
            Thread.Sleep(500);
        }

        // No further logging: the writer must retry on its own once the file is free again.
        WaitForText(path, "written while locked");
    }

    [Fact]
    public void ADeletedLogFile_IsRecreatedWithItsHeaders()
    {
        var path = Path.Combine(directory, "test-2026-10-08.md");
        using (var provider = CreateProvider())
        {
            var logger = provider.CreateLogger("Deletion");
            logger.LogInformation("before deletion");
            WaitForText(path, "before deletion");
            File.Delete(path);
            logger.LogInformation("after deletion");
        }

        var log = File.ReadAllText(path);
        Assert.StartsWith("# 📒 Test Host — log for Thursday, 8 October 2026", log);
        Assert.Contains("This file was deleted while the application was running", log);
        Assert.DoesNotContain("before deletion", log);
        Assert.True(log.IndexOf(MarkdownLogFormatter.TableHeader, StringComparison.Ordinal) < log.IndexOf("after deletion", StringComparison.Ordinal));
        Assert.Contains("### ⏹️ Session ended", log);
    }

    [Fact]
    public void CurrentActivity_TraceId_IsUsedAsCorrelationId()
    {
        string traceId;
        using (var provider = CreateProvider())
        {
            using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
            traceId = activity.TraceId.ToHexString();
            provider.CreateLogger("Http").LogInformation("inside a request");
        }

        Assert.Contains($"| inside a request | `{traceId}` |", ReadSingleLog());
    }

    [Fact]
    public void CorrelationScope_IsUsed_WhenNoActivityExists()
    {
        Assert.Null(Activity.Current);
        using (var provider = CreateProvider())
        using (var factory = LoggerFactory.Create(builder => builder.AddProvider(provider)))
        {
            var logger = factory.CreateLogger("Scoped");
            using (logger.BeginScope(new Dictionary<string, object> { [Correlation.ScopeKey] = "corr-42" }))
            {
                logger.LogInformation("inside scope");
            }

            logger.LogInformation("outside scope");
        }

        var log = ReadSingleLog();
        Assert.Contains("| inside scope | `corr-42` |", log);
        Assert.Contains("| outside scope |  |", log);
    }

    [Fact]
    public async Task RequestLoggingMiddleware_LogsStatusDuration_AndRedactsQuery()
    {
        using (var provider = CreateProvider())
        using (var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(provider)))
        {
            var middleware = new RequestLoggingMiddleware(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }, factory.CreateLogger<RequestLoggingMiddleware>());

            var context = new DefaultHttpContext();
            context.Request.Method = "GET";
            context.Request.Path = "/api/integrations/google/callback";
            context.Request.QueryString = new QueryString("?code=secret-code&state=s1");
            context.TraceIdentifier = "trace-1";
            await middleware.InvokeAsync(context);
        }

        var log = ReadSingleLog();
        Assert.Matches(@"\| ⚠️ WARN \| Logging\.RequestLoggingMiddleware \| GET /api/integrations/google/callback\?code=\*\*\*&state=\*\*\* → 404 in \d+\.\d ms \| `trace-1` \|", log);
        Assert.DoesNotContain("secret-code", log);
    }

    [Fact]
    public async Task RequestLoggingMiddleware_LogsUnhandledExceptions_AndRethrows()
    {
        using (var provider = CreateProvider())
        using (var factory = LoggerFactory.Create(builder => builder.AddProvider(provider)))
        {
            var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("kaput"), factory.CreateLogger<RequestLoggingMiddleware>());
            var context = new DefaultHttpContext();
            context.Request.Method = "POST";
            context.Request.Path = "/api/jobs";
            await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        }

        var log = ReadSingleLog();
        Assert.Contains("POST /api/jobs failed after", log);
        Assert.Contains("with an unhandled InvalidOperationException ⤵", log);
        Assert.Contains("System.InvalidOperationException: kaput", log);
    }

    [Theory]
    [InlineData("logs", @"C:\app", @"C:\app\logs")]
    [InlineData("", @"C:\app", @"C:\app\logs")]
    [InlineData(@"..\shared\logs", @"C:\app\api", @"C:\app\shared\logs")]
    [InlineData(@"D:\logs", @"C:\app", @"D:\logs")]
    public void ResolveDirectory_HandlesRelativeAndAbsolutePaths(string configured, string contentRoot, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, MarkdownFileLoggerProvider.ResolveDirectory(configured, contentRoot));
    }
}
