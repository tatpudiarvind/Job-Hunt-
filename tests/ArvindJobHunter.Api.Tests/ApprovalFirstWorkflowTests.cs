using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ArvindJobHunter.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), $"ajh-api-tests-{Guid.NewGuid():N}");

    public ApiFactory()
    {
        // Program.cs reads Storage:DataDirectory before the host is built; environment variables are the
        // only provider guaranteed to be present at that point under WebApplicationFactory.
        Environment.SetEnvironmentVariable("Storage__DataDirectory", DataDirectory);
        Environment.SetEnvironmentVariable("Runtime__Mode", "DEMO");
        Environment.SetEnvironmentVariable("Runtime__LlmProvider", "Demo");
        // Build the host now, while the variables point at this factory's directory. Otherwise a test that disposes
        // another factory first (which clears the variables) would make this host fall back to a shared folder.
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Environment.SetEnvironmentVariable("Storage__DataDirectory", null);
        Environment.SetEnvironmentVariable("Runtime__Mode", null);
        Environment.SetEnvironmentVariable("Runtime__LlmProvider", null);
        if (Directory.Exists(DataDirectory)) { try { Directory.Delete(DataDirectory, true); } catch { /* best effort */ } }
    }
}

public sealed class ApprovalFirstWorkflowTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiFactory factory;

    public ApprovalFirstWorkflowTests(ApiFactory factory) => this.factory = factory;

    private async Task<HttpClient> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/status");
        var credentials = new { username = "arvind", password = "correct-horse-battery-staple" };
        if (!status.GetProperty("configured").GetBoolean())
        {
            var setup = await client.PostAsJsonAsync("/api/auth/setup", credentials);
            Assert.True(setup.IsSuccessStatusCode, await setup.Content.ReadAsStringAsync());
        }
        var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return client;
    }

    [Fact]
    public async Task Unauthenticated_ProtectedEndpoint_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Host_UsesIsolatedTempDataDirectory()
    {
        var client = await AuthenticatedClientAsync();
        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal(Path.GetFullPath(factory.DataDirectory), Path.GetFullPath(settings.GetProperty("dataDirectory").GetString()!));
        Assert.True(File.Exists(Path.Combine(factory.DataDirectory, "candidate.json")));
    }

    [Fact]
    public async Task Seed_CreatesProfileAndVerifiedFacts()
    {
        var client = await AuthenticatedClientAsync();
        var facts = await client.GetFromJsonAsync<JsonElement>("/api/candidate-profile/facts");
        Assert.True(facts.GetArrayLength() >= 10);
        Assert.All(facts.EnumerateArray(), f => Assert.True(f.GetProperty("isVerified").GetBoolean()));
    }

    [Fact]
    public async Task Settings_CanPersistOpenAiCompatibleConfiguration_AndPreserveStoredApiKey()
    {
        using var isolatedFactory = new ApiFactory();
        var client = isolatedFactory.CreateClient();
        var status = await client.GetFromJsonAsync<JsonElement>("/api/auth/status");
        var credentials = new { username = "arvind", password = "correct-horse-battery-staple" };
        if (!status.GetProperty("configured").GetBoolean())
        {
            var setup = await client.PostAsJsonAsync("/api/auth/setup", credentials);
            Assert.True(setup.IsSuccessStatusCode, await setup.Content.ReadAsStringAsync());
        }

        var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());

        var updated = await client.PutAsJsonAsync("/api/settings", new
        {
            mode = "DEMO",
            llmProvider = "OpenAI",
            llmDisplayName = "Local Gateway",
            llmBaseUrl = "http://localhost:11434/v1/",
            llmApiKey = "secret-key",
            llmModel = "llama3.1:8b",
            masterResumePath = ""
        });
        updated.EnsureSuccessStatusCode();

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("OpenAI", settings.GetProperty("llmProvider").GetString());
        Assert.Equal("Local Gateway", settings.GetProperty("llmDisplayName").GetString());
        Assert.Equal("http://localhost:11434/v1/", settings.GetProperty("llmBaseUrl").GetString());
        Assert.Equal("llama3.1:8b", settings.GetProperty("llmModel").GetString());
        Assert.True(settings.GetProperty("llmConfigured").GetBoolean());

        var preserved = await client.PutAsJsonAsync("/api/settings", new
        {
            mode = "DEMO",
            llmProvider = "OpenAI",
            llmDisplayName = "Local Gateway",
            llmBaseUrl = "http://localhost:11434/v1/",
            llmApiKey = "",
            llmModel = "llama3.1:8b",
            masterResumePath = ""
        });
        preserved.EnsureSuccessStatusCode();
        var preservedBody = await preserved.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-key", preservedBody);
        Assert.DoesNotContain("secret-key", await updated.Content.ReadAsStringAsync());
        Assert.True(JsonDocument.Parse(preservedBody).RootElement.GetProperty("llmConfigured").GetBoolean());

        var persistedText = await File.ReadAllTextAsync(Path.Combine(isolatedFactory.DataDirectory, "settings.json"));
        Assert.Contains("secret-key", persistedText);
    }

    [Fact]
    public async Task FullPipeline_RequiresApprovalBeforeExecution_AndIsIdempotent()
    {
        var client = await AuthenticatedClientAsync();

        var created = await client.PostAsJsonAsync("/api/jobs", new
        {
            title = "Senior .NET Engineer",
            company = "Test GmbH",
            location = "Remote",
            source = "MANUAL",
            description = "Looking for C#, .NET, ASP.NET Core, Azure, microservices, LLMs and RAG experience. Healthcare a plus."
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = await created.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = job.GetProperty("id").GetGuid();
        Assert.Equal("DISCOVERED", job.GetProperty("status").GetString());

        var analyzed = await client.PostAsync($"/api/jobs/{jobId}/analyze", null);
        analyzed.EnsureSuccessStatusCode();
        var pipeline = await analyzed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("QUALIFIED", pipeline.GetProperty("job").GetProperty("status").GetString());
        Assert.InRange(pipeline.GetProperty("job").GetProperty("match").GetProperty("score").GetInt32(), 60, 100);
        Assert.Equal("COMPLETED", pipeline.GetProperty("run").GetProperty("status").GetString());

        var prepared = await client.PostAsync($"/api/jobs/{jobId}/prepare", null);
        prepared.EnsureSuccessStatusCode();
        var preparedResult = await prepared.Content.ReadFromJsonAsync<JsonElement>();
        var approval = preparedResult.GetProperty("resumeApproval");
        Assert.Equal("PENDING", approval.GetProperty("status").GetString());
        var approvalId = approval.GetProperty("id").GetGuid();
        Assert.Equal("DRAFT", preparedResult.GetProperty("coverLetter").GetProperty("status").GetString());

        var applications = await client.GetFromJsonAsync<JsonElement>("/api/applications");
        var app = applications.EnumerateArray().Single(a => a.GetProperty("jobId").GetGuid() == jobId);
        Assert.Equal("AWAITING_APPROVAL", app.GetProperty("status").GetString());

        var key = $"k-{Guid.NewGuid():N}";
        var premature = await client.PostAsJsonAsync($"/api/approvals/{approvalId}/execute", new { idempotencyKey = key });
        Assert.Equal(HttpStatusCode.Conflict, premature.StatusCode);

        var approved = await client.PostAsJsonAsync($"/api/approvals/{approvalId}/approve", new { note = "ok" });
        approved.EnsureSuccessStatusCode();
        Assert.Equal("APPROVED", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var executed = await client.PostAsJsonAsync($"/api/approvals/{approvalId}/execute", new { idempotencyKey = key });
        executed.EnsureSuccessStatusCode();
        var receipt = await executed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SUCCESS", receipt.GetProperty("result").GetString());
        Assert.Equal("DEMO", receipt.GetProperty("mode").GetString());

        var replay = await client.PostAsJsonAsync($"/api/approvals/{approvalId}/execute", new { idempotencyKey = key });
        replay.EnsureSuccessStatusCode();
        var replayReceipt = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(receipt.GetProperty("id").GetGuid(), replayReceipt.GetProperty("id").GetGuid());

        var consumedApproval = await client.GetFromJsonAsync<JsonElement>($"/api/approvals/{approvalId}");
        Assert.Equal("CONSUMED", consumedApproval.GetProperty("status").GetString());

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit-logs");
        var actions = audit.EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToHashSet();
        Assert.Contains("EXECUTION_BLOCKED", actions);
        Assert.Contains("APPROVAL_APPROVED", actions);
        Assert.Contains("EXECUTION_SUCCESS", actions);
    }

    [Fact]
    public async Task ResetPassword_UpdatesStoredCredentials_AndInvalidatesOldSession()
    {
        using var isolatedFactory = new ApiFactory();
        var client = isolatedFactory.CreateClient();
        var signup = await client.PostAsJsonAsync("/api/auth/signup", new { username = "arvind", password = "correct-horse-battery-staple" });
        signup.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "arvind", password = "correct-horse-battery-staple" });
        login.EnsureSuccessStatusCode();
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { username = "arvind", newPassword = "new-correct-horse-battery-staple" });
        reset.EnsureSuccessStatusCode();

        var oldSessionResponse = await client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, oldSessionResponse.StatusCode);

        var oldPasswordLogin = await isolatedFactory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "arvind", password = "correct-horse-battery-staple" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await isolatedFactory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "arvind", password = "new-correct-horse-battery-staple" });
        newPasswordLogin.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Logout_InvalidatesToken()
    {
        var client = await AuthenticatedClientAsync();
        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();
        var after = await client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
