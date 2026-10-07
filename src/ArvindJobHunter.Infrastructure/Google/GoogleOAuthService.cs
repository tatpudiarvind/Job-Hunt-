using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace ArvindJobHunter.Infrastructure.Google;

public sealed class GoogleOAuthOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "http://localhost:5228/api/integrations/google/callback";
    public string Scopes { get; init; } = "https://www.googleapis.com/auth/gmail.compose https://www.googleapis.com/auth/userinfo.email";
}

public sealed class GoogleTokenDocument
{
    public string ProtectedRefreshToken { get; init; } = "";
    public string? AccountEmail { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
}

public sealed class GoogleOAuthService(HttpClient httpClient, IDataProtector protector, IJsonStore<GoogleTokenDocument> store, GoogleOAuthOptions options) : IGoogleOAuthService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset IssuedAt)> states = new();
    private string? cachedAccessToken;
    private DateTimeOffset accessTokenExpiry;

    private bool Configured => !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);

    public async Task<GoogleConnectionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await store.LoadAsync(cancellationToken);
        return new GoogleConnectionStatus(Configured, !string.IsNullOrWhiteSpace(token.ProtectedRefreshToken), token.AccountEmail);
    }

    public async Task<Uri> CreateAuthorizationUriAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!Configured) throw new InvalidOperationException("Google OAuth is not configured. Set Google:ClientId and Google:ClientSecret.");
        PruneStates();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        states[state] = (userId, DateTimeOffset.UtcNow);
        var query = await new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = options.Scopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        }).ReadAsStringAsync(cancellationToken);
        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + query);
    }

    public async Task<bool> CompleteAsync(string code, string state, CancellationToken cancellationToken)
    {
        if (!states.TryRemove(state, out var issued) || issued.IssuedAt.Add(StateLifetime) < DateTimeOffset.UtcNow) return false;
        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = options.RedirectUri,
            ["grant_type"] = "authorization_code"
        }, cancellationToken);
        if (token?.RefreshToken is null) return false;

        var email = await FetchEmailAsync(token.AccessToken, cancellationToken);
        cachedAccessToken = token.AccessToken;
        accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
        await store.SaveAsync(new GoogleTokenDocument { ProtectedRefreshToken = protector.Protect(token.RefreshToken), AccountEmail = email, ConnectedAt = DateTimeOffset.UtcNow }, cancellationToken);
        return true;
    }

    public async Task RevokeAsync(CancellationToken cancellationToken)
    {
        var document = await store.LoadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(document.ProtectedRefreshToken))
        {
            try
            {
                var refresh = protector.Unprotect(document.ProtectedRefreshToken);
                using var response = await httpClient.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or CryptographicException)
            {
            }
        }

        cachedAccessToken = null;
        await store.SaveAsync(new GoogleTokenDocument(), cancellationToken);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (cachedAccessToken is not null && accessTokenExpiry > DateTimeOffset.UtcNow) return cachedAccessToken;
        var document = await store.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(document.ProtectedRefreshToken) || !Configured) return null;
        var refresh = protector.Unprotect(document.ProtectedRefreshToken);
        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refresh,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "refresh_token"
        }, cancellationToken);
        if (token is null) return null;
        cachedAccessToken = token.AccessToken;
        accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60);
        return cachedAccessToken;
    }

    private async Task<TokenResponse?> ExchangeAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    private async Task<string?> FetchEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return (await response.Content.ReadFromJsonAsync<UserInfo>(cancellationToken: cancellationToken))?.Email;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private void PruneStates()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(StateLifetime);
        foreach (var stale in states.Where(s => s.Value.IssuedAt < cutoff).Select(s => s.Key).ToList()) states.TryRemove(stale, out _);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserInfo([property: JsonPropertyName("email")] string? Email);
}
