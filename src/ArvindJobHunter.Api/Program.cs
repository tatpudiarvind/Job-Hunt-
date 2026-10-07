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
using ArvindJobHunter.Infrastructure.Resume;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "data"));
var webBaseUrl = builder.Configuration["Web:BaseUrl"] ?? "http://localhost:4200";

var defaultSettings = new RuntimeSettings
{
    Mode = Enum.TryParse<ExecutionMode>(builder.Configuration["Runtime:Mode"], true, out var mode) ? mode : ExecutionMode.DEMO,
    LlmProvider = builder.Configuration["Runtime:LlmProvider"] ?? "Demo",
    MasterResumePath = builder.Configuration["Runtime:MasterResumePath"] ?? "",
    QualificationThreshold = int.TryParse(builder.Configuration["Runtime:QualificationThreshold"], out var threshold) ? threshold : 60,
    ApprovalTtlHours = int.TryParse(builder.Configuration["Runtime:ApprovalTtlHours"], out var ttl) ? ttl : 24
};

builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(dataDirectory, defaultSettings, new GoogleOAuthOptions
{
    ClientId = builder.Configuration["Google:ClientId"] ?? "",
    ClientSecret = builder.Configuration["Google:ClientSecret"] ?? "",
    RedirectUri = builder.Configuration["Google:RedirectUri"] ?? "http://localhost:5228/api/integrations/google/callback"
},
new WorkerOptions
{
    BaseUrl = builder.Configuration["Worker:BaseUrl"] ?? "",
    SharedSecret = builder.Configuration["Worker:SharedSecret"] ?? ""
});
builder.Services.AddAgents(new OpenAiOptions
{
    ApiKey = builder.Configuration["OpenAI:ApiKey"] ?? "",
    Model = builder.Configuration["OpenAI:Model"] ?? "gpt-4o-mini"
});
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("ArvindJobHunter")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddAuthentication(LocalTokenAuthenticationHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, LocalTokenAuthenticationHandler>(LocalTokenAuthenticationHandler.Scheme, null);
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(webBaseUrl)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

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

await SeedAsync(app.Services);

app.Run();

static async Task SeedAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var profiles = scope.ServiceProvider.GetRequiredService<CandidateProfileService>();
    if (await profiles.GetCurrentAsync(CancellationToken.None) is null)
    {
        await profiles.UpdateAsync(CandidateProfileService.LocalCandidateId, "Arvind Tatpudi", "Senior Software Engineer", "Siemens Healthineers", CancellationToken.None);
    }

    var facts = scope.ServiceProvider.GetRequiredService<CandidateFactService>();
    if ((await facts.ListAsync(CancellationToken.None)).Count == 0)
    {
        foreach (var skill in new[] { "C#", ".NET", "ASP.NET Core", "Microservices", "Azure", "RAG", "LLMs", "Healthcare", "DICOM", "Medical Imaging", "Angular", "WPF", "NUnit", "Integration Testing" })
        {
            var fact = await facts.AddAsync("SKILL", skill, skill, "Initial user-provided profile", LocalUser.Id, CancellationToken.None);
            await facts.SetVerificationAsync(fact.Id, true, LocalUser.Id, CancellationToken.None);
        }
    }
}

public partial class Program;
