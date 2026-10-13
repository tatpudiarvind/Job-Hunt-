using System.Security.Claims;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

public static class ResumeAndEmailEndpoints
{
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/resumes").WithTags("Resumes").RequireAuthorization();

        group.MapGet("", (ResumeService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ResumeService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } version ? Results.Ok(version) : Results.NotFound());

        group.MapPost("/{id:guid}/request-approval", async Task<IResult> (Guid id, ClaimsPrincipal user, ResumeService service, CancellationToken ct) =>
            await service.RequestApprovalAsync(id, user.UserId(), ct) is { } approval ? Results.Ok(approval) : Results.NotFound());

        return endpoints;
    }

    public static IEndpointRouteBuilder MapEmailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/emails").WithTags("Emails").RequireAuthorization();

        group.MapGet("", (EmailDraftService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, EmailDraftService service, CancellationToken ct) =>
            await service.GetAsync(id, ct) is { } draft ? Results.Ok(draft) : Results.NotFound());

        group.MapPost("", async Task<IResult> (CreateEmailDraftRequest request, ClaimsPrincipal user, EmailDraftService service, CancellationToken ct) =>
        {
            try
            {
                var draft = await service.CreateAsync(request.JobId, request.Kind, request.To, request.Subject, request.Body, user.UserId(), ct);
                return Results.Created($"/api/emails/{draft.Id}", draft);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpdateEmailDraftRequest request, ClaimsPrincipal user, EmailDraftService service, CancellationToken ct) =>
            await service.EditAsync(id, request.To, request.Subject, request.Body, user.UserId(), ct) is { } draft ? Results.Ok(draft) : Results.NotFound());

        group.MapPost("/{id:guid}/request-approval", async Task<IResult> (Guid id, bool send, ClaimsPrincipal user, EmailDraftService service, CancellationToken ct) =>
        {
            try
            {
                return await service.RequestApprovalAsync(id, send, user.UserId(), ct) is { } approval ? Results.Ok(approval) : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = [ex.Message] });
            }
        });

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ClaimsPrincipal user, EmailDraftService service, CancellationToken ct) =>
            await service.DeleteAsync(id, user.UserId(), ct) ? Results.NoContent() : Results.NotFound());

        return endpoints;
    }
}
