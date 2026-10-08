using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ArvindJobHunter.Api.Tests;

public sealed class LoggingAndRegressionTests : IClassFixture<ApiFactory>
{
    private const string Password = "correct-horse-battery-staple";
    private readonly ApiFactory factory;

    public LoggingAndRegressionTests(ApiFactory factory) => this.factory = factory;

    private static async Task<HttpClient> SignedInClientAsync(ApiFactory target, WebApplicationFactoryClientOptions? options = null)
    {
        var client = options is null ? target.CreateClient() : target.CreateClient(options);
        var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/status");
        if (!status.GetProperty("configured").GetBoolean())
        {
            (await client.PostAsJsonAsync("/api/auth/setup", new { username = "arvind", password = Password })).EnsureSuccessStatusCode();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "arvind", password = Password });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return client;
    }

    /// <summary>The writer is asynchronous, so poll the day's file(s) until the expected text shows up.</summary>
    private static async Task<string> WaitForLogAsync(string dataDirectory, params string[] expected)
    {
        var folder = Path.Combine(dataDirectory, "logs");
        var content = "";
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Directory.Exists(folder))
            {
                content = string.Concat(Directory.GetFiles(folder, "jobhunter-api-*.md").OrderBy(f => f, StringComparer.Ordinal).Select(ReadShared));
                if (expected.All(text => content.Contains(text, StringComparison.Ordinal))) return content;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"The Markdown log never contained [{string.Join(" | ", expected)}]. Current content:\n{content}");
        return content;
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task MarkdownLog_RecordsEachRequest_UnderTheCorrelationIdReturnedToTheClient()
    {
        var client = await SignedInClientAsync(factory);

        var response = await client.GetAsync("/api/jobs");
        response.EnsureSuccessStatusCode();
        var correlationId = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.Matches("^[0-9a-f]{32}$", correlationId);

        var log = await WaitForLogAsync(factory.DataDirectory, $"GET /api/jobs → 200", $"`{correlationId}`");
        Assert.Contains("# 📒 Job Hunter API — log for", log);
        Assert.Contains("## ▶️ Session started", log);
        Assert.Contains("Job Hunter API ready in Development", log);
        Assert.Contains("Login succeeded for arvind", log);
        Assert.DoesNotContain(Password, log);
        Assert.DoesNotContain(client.DefaultRequestHeaders.Authorization!.Parameter!, log);
    }

    [Fact]
    public async Task MarkdownLog_RecordsFailedRequests_AsWarnings()
    {
        var response = await factory.CreateClient().GetAsync("/api/approvals");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var log = await WaitForLogAsync(factory.DataDirectory, "GET /api/approvals → 401");
        Assert.Contains("| ⚠️ WARN | Logging.RequestLoggingMiddleware | GET /api/approvals → 401", log);
    }

    [Fact]
    public async Task AuditEvents_CarryTheRequestCorrelationId_AndAreMirroredIntoTheLog()
    {
        var client = await SignedInClientAsync(factory);
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Log Test Engineer", company = "Contoso", description = "C# and Azure." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var correlationId = Assert.Single(created.Headers.GetValues("X-Correlation-Id"));

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit-logs");
        var entry = audit.EnumerateArray().First(a => a.GetProperty("action").GetString() == "JOB_CREATED" && a.GetProperty("details").GetString()!.Contains("Log Test Engineer"));
        Assert.Equal(correlationId, entry.GetProperty("correlationId").GetString());

        await WaitForLogAsync(factory.DataDirectory, "Audit JOB_CREATED", "Log Test Engineer @ Contoso");
    }

    [Fact]
    public async Task ClientLogs_AreWrittenToTheMarkdownLog_WithTheBrowserStack()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/client-logs", new
        {
            level = "error",
            message = "TypeError: job.match is undefined",
            url = "http://localhost:4200/jobs/42",
            stack = "TypeError: job.match is undefined\n    at JobDetailComponent.score (main.js:10:5)"
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var log = await WaitForLogAsync(factory.DataDirectory,
            "Browser error on http://localhost:4200/jobs/42: TypeError: job.match is undefined",
            "at JobDetailComponent.score (main.js:10:5)");
        Assert.Contains("| ❌ **ERROR** | Web.Client |", log);
    }

    [Fact]
    public async Task ClientLogs_RequireAMessage()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/client-logs", new { level = "error", message = " " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void GoogleOAuthService_IsASingleton_SoTheCallbackFindsTheIssuedState()
    {
        var first = factory.Services.GetRequiredService<IGoogleOAuthService>();
        var second = factory.Services.GetRequiredService<IGoogleOAuthService>();
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GoogleCallback_WhenConsentIsDenied_RedirectsBackToSettings()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/integrations/google/callback?error=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost:4200/settings?google=failed", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task EmailApprovalToSend_RequiresTheSendFlag_AndAValidRecipient()
    {
        var client = await SignedInClientAsync(factory);
        var created = await client.PostAsJsonAsync("/api/emails", new { kind = "RECRUITER", to = "", subject = "Hello", body = "Short note." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // The query flag is mandatory; the web app used to omit it and always got a 400.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/emails/{id}/request-approval", null)).StatusCode);

        var noRecipient = await client.PostAsync($"/api/emails/{id}/request-approval?send=true", null);
        Assert.Equal(HttpStatusCode.BadRequest, noRecipient.StatusCode);
        Assert.Contains("valid recipient", await noRecipient.Content.ReadAsStringAsync());

        var injected = await client.PutAsJsonAsync($"/api/emails/{id}", new { to = "recruiter@example.com\r\nBcc: everyone@example.com", subject = "Hello", body = "Short note." });
        injected.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/emails/{id}/request-approval?send=true", null)).StatusCode);

        (await client.PutAsJsonAsync($"/api/emails/{id}", new { to = "Recruiter <recruiter@example.com>", subject = "Hello", body = "Short note." })).EnsureSuccessStatusCode();
        var approval = await client.PostAsync($"/api/emails/{id}/request-approval?send=true", null);
        approval.EnsureSuccessStatusCode();
        Assert.Equal("SEND_EMAIL", (await approval.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("actionType").GetString());
    }

    [Fact]
    public async Task MasterResumeUpload_KeepsTheConfiguredLlmProviderAndApiKey()
    {
        using var isolated = new ApiFactory();
        var client = await SignedInClientAsync(isolated);
        (await client.PutAsJsonAsync("/api/settings", new
        {
            mode = "DEMO", llmProvider = "OpenAI", llmDisplayName = "Gateway", llmBaseUrl = "http://localhost:11434/v1/",
            llmApiKey = "upload-test-key", llmModel = "llama3.1:8b", masterResumePath = ""
        })).EnsureSuccessStatusCode();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([0x50, 0x4B, 0x03, 0x04]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        form.Add(file, "file", "My Resume.docx");
        (await client.PostAsync("/api/settings/master-resume", form)).EnsureSuccessStatusCode();

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("OpenAI", settings.GetProperty("llmProvider").GetString());
        Assert.Equal("Gateway", settings.GetProperty("llmDisplayName").GetString());
        Assert.Equal("llama3.1:8b", settings.GetProperty("llmModel").GetString());
        Assert.True(settings.GetProperty("llmConfigured").GetBoolean());
        Assert.EndsWith("master-My Resume.docx", settings.GetProperty("masterResumePath").GetString());
        Assert.Contains("upload-test-key", await File.ReadAllTextAsync(Path.Combine(isolated.DataDirectory, "settings.json")));
    }

    [Fact]
    public async Task AgentFailure_Returns502_AndPersistsTheFailedRunWithItsToolCall()
    {
        using var isolated = new ApiFactory();
        var client = await SignedInClientAsync(isolated);
        // Nothing listens on port 9, so the OpenAI-compatible call fails fast with a connection error.
        (await client.PutAsJsonAsync("/api/settings", new
        {
            mode = "DEMO", llmProvider = "OpenAI", llmDisplayName = "Unreachable", llmBaseUrl = "http://127.0.0.1:9/v1/",
            llmApiKey = "test-key", llmModel = "test-model", masterResumePath = ""
        })).EnsureSuccessStatusCode();
        var created = await client.PostAsJsonAsync("/api/jobs", new { title = "Engineer", company = "Contoso", description = "C# and Azure." });
        var jobId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var analyzed = await client.PostAsync($"/api/jobs/{jobId}/analyze", null);

        Assert.Equal(HttpStatusCode.BadGateway, analyzed.StatusCode);
        var problem = await analyzed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Agent run failed", problem.GetProperty("title").GetString());
        var runId = problem.GetProperty("runId").GetGuid();
        var run = await client.GetFromJsonAsync<JsonElement>($"/api/agent-runs/{runId}");
        Assert.Equal("FAILED", run.GetProperty("status").GetString());
        var toolCall = Assert.Single(run.GetProperty("toolCalls").EnumerateArray());
        Assert.Equal("AnalyzeJobTool", toolCall.GetProperty("toolName").GetString());
        Assert.False(toolCall.GetProperty("succeeded").GetBoolean());

        var log = await WaitForLogAsync(isolated.DataDirectory, "Tool AnalyzeJobTool failed", $"Agent run {runId} FAILED", $"POST /api/jobs/{jobId}/analyze → 502");
        Assert.DoesNotContain("test-key", log);
    }
}
