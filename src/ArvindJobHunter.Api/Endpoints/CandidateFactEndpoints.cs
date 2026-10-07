using System.Security.Claims;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

public static class CandidateFactEndpoints
{
    public static IEndpointRouteBuilder MapCandidateFactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidate-profile/facts").WithTags("Candidate Facts").RequireAuthorization();

        group.MapGet("", (CandidateFactService service, CancellationToken ct) => service.ListAsync(ct));

        group.MapPost("", async Task<IResult> (CreateFactRequest request, ClaimsPrincipal user, CandidateFactService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.FactType) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Value))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["fact"] = ["Fact type, name, and value are required."] });
            }

            return Results.Ok(await service.AddAsync(request.FactType, request.Name, request.Value, request.SourceReference, user.UserId(), ct));
        });

        group.MapPost("/{id:guid}/verify", async Task<IResult> (Guid id, ClaimsPrincipal user, CandidateFactService service, CancellationToken ct) =>
            await service.SetVerificationAsync(id, true, user.UserId(), ct) is { } fact ? Results.Ok(fact) : Results.NotFound());

        group.MapPost("/{id:guid}/invalidate", async Task<IResult> (Guid id, ClaimsPrincipal user, CandidateFactService service, CancellationToken ct) =>
            await service.SetVerificationAsync(id, false, user.UserId(), ct) is { } fact ? Results.Ok(fact) : Results.NotFound());

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, ClaimsPrincipal user, CandidateFactService service, CancellationToken ct) =>
            await service.DeleteAsync(id, user.UserId(), ct) ? Results.NoContent() : Results.NotFound());

        return endpoints;
    }
}
