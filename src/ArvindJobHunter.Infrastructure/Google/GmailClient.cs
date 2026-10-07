using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Infrastructure.Google;

/// <summary>Gmail REST adapter. Only reachable through ExecuteApprovedActionCommand in LIVE mode.</summary>
public sealed class GmailClient(HttpClient httpClient, IGoogleOAuthService oauth) : IGmailClient
{
    private const string BaseUrl = "https://gmail.googleapis.com/gmail/v1/users/me/";

    public Task<GmailResult> CreateDraftAsync(string to, string subject, string body, CancellationToken cancellationToken) =>
        PostAsync("drafts", new { message = new { raw = Encode(to, subject, body) } }, cancellationToken);

    public Task<GmailResult> SendAsync(string to, string subject, string body, CancellationToken cancellationToken) =>
        PostAsync("messages/send", new { raw = Encode(to, subject, body) }, cancellationToken);

    private async Task<GmailResult> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var token = await oauth.GetAccessTokenAsync(cancellationToken);
        if (token is null) return new GmailResult(false, null, "Gmail is not connected.");

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new GmailResult(false, null, $"Gmail returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }

        var result = await response.Content.ReadFromJsonAsync<GmailIdResponse>(cancellationToken: cancellationToken);
        return new GmailResult(true, result?.Id ?? "", null);
    }

    private static string Encode(string to, string subject, string body)
    {
        var mime = $"To: {to}\r\nSubject: =?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=\r\nContent-Type: text/plain; charset=UTF-8\r\nMIME-Version: 1.0\r\n\r\n{body}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private sealed record GmailIdResponse([property: JsonPropertyName("id")] string? Id);
}
