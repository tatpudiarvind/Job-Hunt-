using System.Security.Claims;
using ArvindJobHunter.Api.Auth;
using ArvindJobHunter.Application.Abstractions;
using ArvindJobHunter.Contracts.Api;

namespace ArvindJobHunter.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth").WithTags("Auth").AllowAnonymous();

        group.MapGet("/status", async (HttpContext context, ILocalAuthenticationService service, CancellationToken ct) =>
            Results.Ok(new AuthStatusResponse(await service.IsConfiguredAsync(ct), context.User.Identity?.IsAuthenticated == true)));

        group.MapPost("/setup", async Task<IResult> (AuthRequest request, ILocalAuthenticationService service, CancellationToken ct) =>
            await service.SetupAsync(request.Username, request.Password, ct)
                ? Results.NoContent()
                : Results.BadRequest(new { error = "Setup requires a username and a password of at least 12 characters, and can only run once." }));

        group.MapPost("/login", async Task<IResult> (AuthRequest request, ILocalAuthenticationService service, CancellationToken ct) =>
        {
            var session = await service.LoginAsync(request.Username, request.Password, ct);
            return session is null ? Results.Unauthorized() : Results.Ok(new SessionResponse(session.Token, session.ExpiresAt, session.UserId));
        }).RequireRateLimiting("auth");

        group.MapPost("/logout", (ClaimsPrincipal user, ILocalAuthenticationService service) =>
        {
            if (user.SessionToken() is { } token) service.Logout(token);
            return Results.NoContent();
        });

        return endpoints;
    }
}
