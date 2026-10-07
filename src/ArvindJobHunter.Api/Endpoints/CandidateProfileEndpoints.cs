using ArvindJobHunter.Application.Features;
using ArvindJobHunter.Contracts.Api;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ArvindJobHunter.Api.Endpoints;

public static class CandidateProfileEndpoints
{
    public static IEndpointRouteBuilder MapCandidateProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidate-profile").WithTags("Candidate Profile").RequireAuthorization();

        group.MapGet("", async Task<Results<Ok<CandidateProfileResponse>, NotFound>> (
            CandidateProfileService service,
            CancellationToken cancellationToken) =>
        {
            var profile = await service.GetCurrentAsync(cancellationToken);
            return profile is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(ToResponse(profile));
        });

        group.MapPut("", async Task<Results<Ok<CandidateProfileResponse>, ValidationProblem>> (
            UpdateCandidateProfileRequest request,
            CandidateProfileService service,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.CurrentRole))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["profile"] = ["Name and current role are required."]
                });
            }

            var profile = await service.UpdateAsync(
                CandidateProfileService.LocalCandidateId,
                request.Name,
                request.CurrentRole,
                request.CurrentCompany,
                cancellationToken);
            return TypedResults.Ok(ToResponse(profile));
        });

        return endpoints;
    }

    private static CandidateProfileResponse ToResponse(ArvindJobHunter.Domain.Entities.CandidateProfile profile) =>
        new(profile.Id, profile.Name, profile.CurrentRole, profile.CurrentCompany,
            profile.CreatedAt, profile.UpdatedAt, Convert.ToBase64String(profile.Version));
}
