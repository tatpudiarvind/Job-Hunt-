using System.Security.Claims;
using ArvindJobHunter.Agents;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/jobs").WithTags("Jobs").RequireAuthorization();

        group.MapGet("", (JobService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, JobService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } job ? Results.Ok(job) : Results.NotFound());

        group.MapPost("", async Task<IResult> (CreateJobRequest request, ClaimsPrincipal user, JobService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Company) || string.IsNullOrWhiteSpace(request.Description))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["job"] = ["Title, company, and description are required."] });
            }

            var job = await service.CreateAsync(request.Title, request.Company, request.Location, request.Source, request.Url, request.Description, user.UserId(), ct);
            return Results.Created($"/api/jobs/{job.Id}", job);
        });

        group.MapPost("/import-preview", async Task<IResult> (ImportJobUrlRequest request, IJobPostingFetcher fetcher, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = ["Enter a valid absolute URL."] });
            }

            try
            {
                return Results.Ok(await fetcher.FetchAsync(uri, ct));
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                return Results.BadRequest(new { error = $"Could not fetch the posting: {ex.Message}" });
            }
        });

        group.MapPost("/{id:guid}/analyze", async Task<IResult> (Guid id, ClaimsPrincipal user, AgentOrchestrator orchestrator, CancellationToken ct) =>
        {
            try
            {
                var result = await orchestrator.AnalyzeAndMatchAsync(id, user.UserId(), ct);
                return Results.Ok(new { result.Job, result.Run });
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (AgentRunFailedException ex) { return AgentFailed(ex); }
        });

        group.MapPost("/{id:guid}/prepare", async Task<IResult> (Guid id, ClaimsPrincipal user, AgentOrchestrator orchestrator, CancellationToken ct) =>
        {
            try
            {
                var result = await orchestrator.PrepareApplicationAsync(id, user.UserId(), ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (AgentRunFailedException ex) { return AgentFailed(ex); }
        });

        group.MapPost("/{id:guid}/recruiter-email", async Task<IResult> (Guid id, RecruiterEmailRequest request, ClaimsPrincipal user, AgentOrchestrator orchestrator, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await orchestrator.DraftRecruiterEmailAsync(id, request.Recipient ?? "", user.UserId(), ct));
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (AgentRunFailedException ex) { return AgentFailed(ex); }
        });

        group.MapGet("/{id:guid}/interview-prep", async Task<IResult> (Guid id, JobService service, InterviewPrepService prep, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } job ? Results.Ok(prep.Build(job)) : Results.NotFound());

        group.MapPost("/{id:guid}/dismiss", async Task<IResult> (Guid id, ClaimsPrincipal user, JobService service, CancellationToken ct) =>
            await service.DismissAsync(id, user.UserId(), ct) is { } job ? Results.Ok(job) : Results.NotFound());

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ClaimsPrincipal user, JobService service, CancellationToken ct) =>
            await service.DeleteAsync(id, user.UserId(), ct) ? Results.NoContent() : Results.NotFound());

        return endpoints;
    }

    /// <summary>The LLM or a tool failed (bad API key, timeout, unusable output). The failed run is recorded under Activity.</summary>
    private static IResult AgentFailed(AgentRunFailedException ex) =>
        Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "Agent run failed",
            extensions: new Dictionary<string, object?> { ["runId"] = ex.RunId });

    public sealed record RecruiterEmailRequest(string? Recipient);
    public sealed record ImportJobUrlRequest(string Url);
}
