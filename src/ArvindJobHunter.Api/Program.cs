using System.Threading.RateLimiting;
using ArvindJobHunter.Agents;
using ArvindJobHunter.Agents.Llm;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Api.Endpoints;
using ArvindJobHunter.Application;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Domain;
using ArvindJobHunter.Infrastructure;
using ArvindJobHunter.Infrastructure.Google;
using ArvindJobHunter.Infrastructure.Persistence;
using ArvindJobHunter.Infrastructure.Resume;
using ArvindJobHunter.Logging;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"));
var webBaseUrl = builder.Configuration["Web:BaseUrl"] ?? "http://localhost:4200";
var appSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");

// Human-readable Markdown log (one file per day) next to the data it describes; Logging:MarkdownFile overrides these defaults.
builder.Logging.AddMarkdownFile(options =>
{
    options.Directory = Path.Combine(dataDirectory, "logs");
    options.FileNamePrefix = "jobhunter-api";
    options.Title = "Job Hunter API";
});

var defaultSettings = new RuntimeSettings
{
    Mode = Enum.TryParse<ExecutionMode>(builder.Configuration["Runtime:Mode"], true, out var mode) ? mode : ExecutionMode.DEMO,
    LlmProvider = builder.Configuration["Runtime:LlmProvider"] ?? "Demo",
    LlmDisplayName = builder.Configuration["Runtime:LlmDisplayName"] ?? builder.Configuration["Runtime:LlmProvider"] ?? "Demo",
    LlmBaseUrl = builder.Configuration["Runtime:LlmBaseUrl"] ?? builder.Configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/",
    LlmApiKey = builder.Configuration["Runtime:LlmApiKey"] ?? builder.Configuration["OpenAI:ApiKey"] ?? "",
    LlmModel = builder.Configuration["Runtime:LlmModel"] ?? builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini",
    MasterResumePath = builder.Configuration["Runtime:MasterResumePath"] ?? "",
    QualificationThreshold = int.TryParse(builder.Configuration["Runtime:QualificationThreshold"], out var threshold) ? threshold : 60,
    ApprovalTtlHours = int.TryParse(builder.Configuration["Runtime:ApprovalTtlHours"], out var ttl) ? ttl : 24
};

builder.Services.AddProblemDetails();
// Malformed requests (e.g. a missing required query parameter) are client errors: answer 400, not 500, in every environment.
builder.Services.AddExceptionHandler(options => options.StatusCodeSelector = exception =>
    exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(dataDirectory, defaultSettings, appSettingsPath, new GoogleOAuthConfiguration(
    builder.Configuration["Google:ClientId"] ?? "",
    builder.Configuration["Google:ClientSecret"] ?? "",
    builder.Configuration["Google:RedirectUri"] ?? "http://localhost:5228/api/integrations/google/callback"),
new WorkerOptions
{ 
    BaseUrl = builder.Configuration["Worker:BaseUrl"] ?? "",
    SharedSecret = builder.Configuration["Worker:SharedSecret"] ?? ""
});
builder.Services.AddAgents(new OpenAiOptions
{
    ApiKey = builder.Configuration["OpenAI:ApiKey"] ?? builder.Configuration["Runtime:LlmApiKey"] ?? "",
    Model = builder.Configuration["OpenAI:Model"] ?? builder.Configuration["Runtime:LlmModel"] ?? "gpt-4o-mini",
    BaseUrl = builder.Configuration["OpenAI:BaseUrl"] ?? builder.Configuration["Runtime:LlmBaseUrl"] ?? "https://api.openai.com/v1/",
    DisplayName = builder.Configuration["Runtime:LlmDisplayName"] ?? "OpenAI"
});
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("ArvindJobHunter")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddAuthentication(LocalTokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LocalTokenAuthenticationHandler>(LocalTokenAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("client-logs", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ArvindJobHunter.Api.RateLimiting")
            .LogWarning("Rate limit exceeded for {Method} {Path} from {RemoteIp}", context.HttpContext.Request.Method, context.HttpContext.Request.Path, context.HttpContext.Connection.RemoteIpAddress);
        return ValueTask.CompletedTask;
    };
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(webBaseUrl)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(Correlation.HeaderName)));

var app = builder.Build();
app.LogUnhandledExceptions("ArvindJobHunter.Api");

// First, so every request (including ones turned into 500s by the exception handler) gets one log line and a correlation id.
app.UseMarkdownRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", phase = "phase-3-approval-first-core" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapCandidateProfileEndpoints();
app.MapCandidateFactEndpoints();
app.MapJobEndpoints();
app.MapApprovalEndpoints();
app.MapApplicationEndpoints();
app.MapResumeEndpoints();
app.MapEmailEndpoints();
app.MapReadModelEndpoints(dataDirectory);
app.MapClientLogEndpoints();

await SeedAsync(app.Services);
await LogStartupSummaryAsync(app.Services, dataDirectory);

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ArvindJobHunter.Api.Startup");
    var profiles = scope.ServiceProvider.GetRequiredService<CandidateProfileService>();
    if (await profiles.GetCurrentAsync(CancellationToken.None) is null)
    {
        await profiles.UpdateAsync(CandidateProfileService.LocalCandidateId, "Arvind Tatpudi", "Senior Software Engineer", "Siemens Healthineers", CancellationToken.None);
        logger.LogInformation("Seeded the default candidate profile");
    }

    var facts = scope.ServiceProvider.GetRequiredService<CandidateFactService>();
    if ((await facts.ListAsync(CancellationToken.None)).Count == 0)
    {
        var skills = new[] { "C#", ".NET", "ASP.NET Core", "Microservices", "Azure", "RAG", "LLMs", "Healthcare", "DICOM", "Medical Imaging", "Angular", "WPF", "NUnit", "Integration Testing" };
        foreach (var skill in skills)
        {
            var fact = await facts.AddAsync("SKILL", skill, skill, "Initial user-provided profile", LocalUser.Id, CancellationToken.None);
            await facts.SetVerificationAsync(fact.Id, true, LocalUser.Id, CancellationToken.None);
        }

        logger.LogInformation("Seeded {Count} verified skill facts", skills.Length);
    }
}

static async Task LogStartupSummaryAsync(IServiceProvider services, string dataDirectory)
{
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ArvindJobHunter.Api.Startup");
    var environment = services.GetRequiredService<IHostEnvironment>();
    var settings = await services.GetRequiredService<IRuntimeSettingsProvider>().GetAsync(CancellationToken.None);
    var google = await services.GetRequiredService<IGoogleOAuthSettingsProvider>().GetAsync(CancellationToken.None);
    var worker = services.GetRequiredService<WorkerOptions>();

    logger.LogInformation("Job Hunter API ready in {Environment}. Data directory: {DataDirectory}. Markdown logs: {LogDirectory}",
        environment.EnvironmentName, dataDirectory, services.GetMarkdownLogger()?.LogDirectory ?? "(disabled)");
    logger.LogInformation("Runtime settings: {Settings}{MasterResumeState}", JsonRuntimeSettingsProvider.Describe(settings),
        string.IsNullOrWhiteSpace(settings.MasterResumePath) || File.Exists(settings.MasterResumePath) ? "" : " (master resume file not found!)");
    logger.LogInformation("Integrations: Google OAuth {GoogleState}; resume worker {WorkerState}",
        string.IsNullOrWhiteSpace(google.ClientId) || string.IsNullOrWhiteSpace(google.ClientSecret) ? "not configured" : "configured",
        worker.Enabled ? $"enabled at {worker.BaseUrl}" : "disabled (Open XML runs in-process)");
    if (settings.Mode == ExecutionMode.LIVE)
    {
        logger.LogWarning("LIVE mode is active: executing an approved action writes real resume files and uses Gmail");
    }
}

public partial class Program;
