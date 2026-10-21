using System.Security.Claims;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;
using ArvindJobHunter.Domain;

namespace ArvindJobHunter.Api.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/applications").WithTags("Applications").RequireAuthorization();

        group.MapGet("", (ApplicationService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ApplicationService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } application ? Results.Ok(application) : Results.NotFound());

        group.MapPost("/{id:guid}/transition", async Task<IResult> (Guid id, TransitionApplicationRequest request, ClaimsPrincipal user, ApplicationService service, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ApplicationStatus>(request.Status, true, out var status))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [$"Unknown status '{request.Status}'."] });
            }

            try
            {
                return await service.TransitionAsync(id, status, request.Reason ?? "Manual update", user.UserId(), ct) is { } updated ? Results.Ok(updated) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Invalid transition");
            }
        });

        group.MapGet("/{id:guid}/review", async Task<IResult> (Guid id, ApplicationPackageService packages, CancellationToken ct) =>
            await packages.ReviewAsync(id, ct) is { } review ? Results.Ok(review) : Results.NotFound());

        group.MapGet("/{id:guid}/packages", (Guid id, ApplicationPackageService packages, CancellationToken ct) => packages.HistoryAsync(id, ct));

        group.MapPost("/{id:guid}/packages", async Task<IResult> (Guid id, ClaimsPrincipal user, ApplicationPackageService packages, CancellationToken ct) =>
        {
            try
            {
                return await packages.CreatePackageAsync(id, user.UserId(), ct) is { } package ? Results.Ok(package) : Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Application package is not consistent");
            }
        });

        group.MapGet("/statuses", () => Enum.GetNames<ApplicationStatus>());

        group.MapGet("/follow-ups/due", (ApplicationService service, CancellationToken ct) => service.DueFollowUpsAsync(ct));

        group.MapPut("/{id:guid}/follow-up", async Task<IResult> (Guid id, ScheduleFollowUpRequest request, ClaimsPrincipal user, ApplicationService service, CancellationToken ct) =>
        {
            if (request.DueAt <= DateTimeOffset.UtcNow.AddMinutes(-1))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dueAt"] = ["Follow-up time must be in the future."] });
            }

            return await service.ScheduleFollowUpAsync(id, request.DueAt, request.Note, user.UserId(), ct) is { } updated ? Results.Ok(updated) : Results.NotFound();
        });

        group.MapDelete("/{id:guid}/follow-up", async Task<IResult> (Guid id, ClaimsPrincipal user, ApplicationService service, CancellationToken ct) =>
            await service.ClearFollowUpAsync(id, user.UserId(), ct) is { } updated ? Results.Ok(updated) : Results.NotFound());

        group.MapPost("/{id:guid}/follow-up-email", async Task<IResult> (Guid id, ClaimsPrincipal user, FollowUpDraftService drafts, CancellationToken ct) =>
        {
            try { return await drafts.DraftAsync(id, user.UserId(), ct) is { } draft ? Results.Ok(draft) : Results.NotFound(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });

        return endpoints;
    }

    public sealed record ScheduleFollowUpRequest(DateTimeOffset DueAt, string? Note);
}
