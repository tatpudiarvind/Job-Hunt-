using System.Security.Claims;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/approvals").WithTags("Approvals").RequireAuthorization();

        group.MapGet("", (ApprovalService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ApprovalService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } approval ? Results.Ok(approval) : Results.NotFound());

        group.MapPost("/{id:guid}/approve", async Task<IResult> (Guid id, ApprovalDecisionRequest? request, ClaimsPrincipal user, ApprovalService service, CancellationToken ct) =>
            await Decide(id, true, request?.Note, user, service, ct));

        group.MapPost("/{id:guid}/reject", async Task<IResult> (Guid id, ApprovalDecisionRequest? request, ClaimsPrincipal user, ApprovalService service, CancellationToken ct) =>
            await Decide(id, false, request?.Note, user, service, ct));

        group.MapPost("/{id:guid}/execute", async Task<IResult> (Guid id, ExecuteApprovalRequest request, ClaimsPrincipal user, ExecuteApprovedActionCommand command, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await command.ExecuteAsync(id, request.IdempotencyKey, user.UserId(), ct));
            }
            catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
            catch (ApprovalViolationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Execution blocked"); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapGet("/receipts", (ExecuteApprovedActionCommand command, CancellationToken ct) => command.ListAsync(ct));

        return endpoints;
    }

    private static async Task<IResult> Decide(Guid id, bool approved, string? note, ClaimsPrincipal user, ApprovalService service, CancellationToken ct)
    {
        try
        {
            return await service.DecideAsync(id, approved, note, user.UserId(), ct) is { } result ? Results.Ok(result) : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
