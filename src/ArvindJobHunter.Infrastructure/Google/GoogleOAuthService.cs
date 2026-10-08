using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

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

/// <summary>
/// Google OAuth for Gmail. Must be registered as a singleton: the <c>state</c> issued by <see cref="CreateAuthorizationUriAsync"/>
/// has to still be known when Google redirects back to the callback, and the access token is cached between calls.
/// </summary>
public sealed class GoogleOAuthService(
    IHttpClientFactory httpClientFactory,
    IDataProtector protector,
    IJsonStore<GoogleTokenDocument> store,
    IGoogleOAuthSettingsProvider settingsProvider,
    ILogger<GoogleOAuthService> logger) : IGoogleOAuthService
{
    public const string HttpClientName = "GoogleOAuth";
    private const string Scopes = "https://www.googleapis.com/auth/gmail.compose https://www.googleapis.com/auth/userinfo.email";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset IssuedAt)> states = new();
    private volatile CachedAccessToken? cachedAccessToken;

    private HttpClient Http => httpClientFactory.CreateClient(HttpClientName);

    public async Task<GoogleConnectionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await store.LoadAsync(cancellationToken);
        var options = await settingsProvider.GetAsync(cancellationToken);
        var configured = !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);
        return new GoogleConnectionStatus(configured, !string.IsNullOrWhiteSpace(token.ProtectedRefreshToken), token.AccountEmail);
    }

    public async Task<Uri> CreateAuthorizationUriAsync(Guid userId, CancellationToken cancellationToken)
    {
        var options = await settingsProvider.GetAsync(cancellationToken);
        if (!IsConfigured(options))
        {
            logger.LogWarning("Google authorization requested, but the Google OAuth client ID/secret are not configured");
            throw new InvalidOperationException("Google OAuth is not configured. Set Google:ClientId and Google:ClientSecret.");
        }

        PruneStates();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        states[state] = (userId, DateTimeOffset.UtcNow);
        var query = await new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = options.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["state"] = state
        }).ReadAsStringAsync(cancellationToken);
        logger.LogInformation("Google authorization started; Google will redirect back to {RedirectUri}", options.RedirectUri);
        return new Uri("https://accounts.google.com/o/oauth2/v2/auth?" + query);
    }

    public async Task<bool> CompleteAsync(string code, string state, CancellationToken cancellationToken)
    {
        var options = await settingsProvider.GetAsync(cancellationToken);
        if (!states.TryRemove(state, out var issued))
        {
            logger.LogWarning("Google OAuth callback rejected: unknown state (the API restarted, or the link was already used). Start the connection again from Settings");
            return false;
        }

        if (issued.IssuedAt.Add(StateLifetime) < DateTimeOffset.UtcNow)
        {
            logger.LogWarning("Google OAuth callback rejected: the authorization request is older than {Minutes} minutes", StateLifetime.TotalMinutes);
            return false;
        }

        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["redirect_uri"] = options.RedirectUri,
            ["grant_type"] = "authorization_code"
        }, "authorization-code exchange", cancellationToken);
        if (token is null) return false;
        if (token.RefreshToken is null)
        {
            logger.LogWarning("Google returned no refresh token. Remove the app at https://myaccount.google.com/permissions and connect again");
            return false;
        }

        var email = await FetchEmailAsync(token.AccessToken, cancellationToken);
        cachedAccessToken = new CachedAccessToken(token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60));
        await store.SaveAsync(new GoogleTokenDocument { ProtectedRefreshToken = protector.Protect(token.RefreshToken), AccountEmail = email, ConnectedAt = DateTimeOffset.UtcNow }, cancellationToken);
        logger.LogInformation("Google account {AccountEmail} connected for Gmail", email ?? "(email not shared)");
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
                using var response = await Http.PostAsync("https://oauth2.googleapis.com/revoke", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refresh }), cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Google token revocation returned {StatusCode}; the local tokens are removed anyway", (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or CryptographicException)
            {
                logger.LogWarning(ex, "Google token revocation failed; the local tokens are removed anyway");
            }
        }

        cachedAccessToken = null;
        await store.SaveAsync(new GoogleTokenDocument(), cancellationToken);
        logger.LogInformation("Google account {AccountEmail} disconnected", document.AccountEmail ?? "(unknown)");
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (cachedAccessToken is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow) return cached.Value;
        var document = await store.LoadAsync(cancellationToken);
        var options = await settingsProvider.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(document.ProtectedRefreshToken))
        {
            logger.LogWarning("A Gmail access token was requested, but no Google account is connected");
            return null;
        }

        if (!IsConfigured(options))
        {
            logger.LogWarning("A Google account is connected, but the Google OAuth client ID/secret are missing");
            return null;
        }

        string refresh;
        try
        {
            refresh = protector.Unprotect(document.ProtectedRefreshToken);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "The stored Google refresh token cannot be decrypted (were the DataProtection keys replaced?). Reconnect Gmail in Settings");
            return null;
        }

        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refresh,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "refresh_token"
        }, "access-token refresh", cancellationToken);
        if (token is null) return null;
        cachedAccessToken = new CachedAccessToken(token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn - 60));
        logger.LogDebug("Google access token refreshed; valid for {Minutes} minutes", token.ExpiresIn / 60);
        return token.AccessToken;
    }

    private static bool IsConfigured(GoogleOAuthConfiguration options) => !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);

    private async Task<TokenResponse?> ExchangeAsync(Dictionary<string, string> form, string purpose, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Google explains failures such as invalid_grant or redirect_uri_mismatch in the body; it never contains tokens.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Google token {Purpose} failed with {StatusCode}: {Body}", purpose, (int)response.StatusCode, body.Length > 500 ? body[..500] + "…" : body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
    }

    private async Task<string?> FetchEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openidconnect.googleapis.com/v1/userinfo");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google userinfo returned {StatusCode}; the account email is unknown", (int)response.StatusCode);
                return null;
            }

            return (await response.Content.ReadFromJsonAsync<UserInfo>(cancellationToken: cancellationToken))?.Email;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Google userinfo request failed; the account email is unknown");
            return null;
        }
    }

    private void PruneStates()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(StateLifetime);
        foreach (var stale in states.Where(s => s.Value.IssuedAt < cutoff).Select(s => s.Key).ToList()) states.TryRemove(stale, out _);
    }

    private sealed record CachedAccessToken(string Value, DateTimeOffset ExpiresAt);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record UserInfo([property: JsonPropertyName("email")] string? Email);
}
