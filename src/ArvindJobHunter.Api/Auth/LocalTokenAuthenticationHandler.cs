using System.Security.Claims;
using System.Text.Encodings.Web;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ArvindJobHunter.Api.Auth;

public sealed class LocalTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ILocalAuthenticationService authentication)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "LocalToken";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0 || !authentication.TryGetUser(token, out var session))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired session."));
        }

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new Claim("token", session.Token)
        ], Scheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Domain.LocalUser.Id;

    public static string? SessionToken(this ClaimsPrincipal principal) => principal.FindFirstValue("token");
}
