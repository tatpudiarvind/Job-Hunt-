using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

    public async Task<IReadOnlyList<GmailLabel>> ListLabelsAsync(CancellationToken cancellationToken)
    {
        var token = await oauth.GetAccessTokenAsync(cancellationToken);
        if (token is null) return Array.Empty<GmailLabel>();

        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "labels");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token expired, try refresh
            token = await oauth.GetAccessTokenAsync(cancellationToken);
            if (token is null) return Array.Empty<GmailLabel>();

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "labels");
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode) return Array.Empty<GmailLabel>();

            var retryResult = await retryResponse.Content.ReadFromJsonAsync<LabelsResponse>(cancellationToken: cancellationToken);
            return retryResult?.Labels?.Select(l => new GmailLabel(l.Id, l.Name, l.MessagesTotal, l.ThreadsTotal)).ToList() as IReadOnlyList<GmailLabel> ?? Array.Empty<GmailLabel>();
        }

        if (!response.IsSuccessStatusCode) return Array.Empty<GmailLabel>();

        var result = await response.Content.ReadFromJsonAsync<LabelsResponse>(cancellationToken: cancellationToken);
        return result?.Labels?.Select(l => new GmailLabel(l.Id, l.Name, l.MessagesTotal, l.ThreadsTotal)).ToList() as IReadOnlyList<GmailLabel> ?? Array.Empty<GmailLabel>();
    }

    public async Task<GmailSearchResults> ListMessagesAsync(int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default)
    {
        var query = BuildQuery(null, maxResults, pageToken);
        return await SearchAsync(query, cancellationToken);
    }

    public async Task<GmailSearchResults> SearchMessagesAsync(string query, int? maxResults = null, string? pageToken = null, CancellationToken cancellationToken = default)
    {
        var fullQuery = BuildQuery(query, maxResults, pageToken);
        return await SearchAsync(fullQuery, cancellationToken);
    }

    public async Task<GmailMessage?> GetMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        var token = await oauth.GetAccessTokenAsync(cancellationToken);
        if (token is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}messages/{Uri.EscapeDataString(messageId)}?format=full");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token expired, try refresh
            token = await oauth.GetAccessTokenAsync(cancellationToken);
            if (token is null) return null;

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}messages/{Uri.EscapeDataString(messageId)}?format=full");
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode) return null;

            var retryResult = await retryResponse.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken: cancellationToken);
            return ParseMessage(retryResult);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<MessageResponse>(cancellationToken: cancellationToken);
        return ParseMessage(result);
    }

    private async Task<GmailSearchResults> SearchAsync(string queryString, CancellationToken cancellationToken)
    {
        var token = await oauth.GetAccessTokenAsync(cancellationToken);
        if (token is null) return new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}messages?{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token expired, try refresh
            token = await oauth.GetAccessTokenAsync(cancellationToken);
            if (token is null) return new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0);

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}messages?{queryString}");
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode) return new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0);

            var retryResult = await retryResponse.Content.ReadFromJsonAsync<MessagesListResponse>(cancellationToken: cancellationToken);
            return await ProcessSearchResults(retryResult, cancellationToken);
        }

        if (!response.IsSuccessStatusCode) return new GmailSearchResults(Array.Empty<GmailMessageSummary>(), null, 0);

        var result = await response.Content.ReadFromJsonAsync<MessagesListResponse>(cancellationToken: cancellationToken);
        return await ProcessSearchResults(result, cancellationToken);
    }

    private async Task<GmailSearchResults> ProcessSearchResults(MessagesListResponse? result, CancellationToken cancellationToken)
    {
        if (result?.Messages == null || result.Messages.Count == 0)
            return new GmailSearchResults(Array.Empty<GmailMessageSummary>(), result?.NextPageToken, result?.ResultSizeEstimate ?? 0);

        // Fetch full messages to get headers
        var summaries = new List<GmailMessageSummary>();
        foreach (var msg in result.Messages)
        {
            var full = await GetMessageAsync(msg.Id, cancellationToken);
            if (full != null)
            {
                summaries.Add(new GmailMessageSummary(
                    full.Id,
                    full.ThreadId,
                    full.Labels,
                    full.IsSpam,
                    full.IsStarred,
                    full.IsUnread,
                    full.ReceivedAt,
                    GetHeaderFromPayload(full.Payload, "From"),
                    GetHeaderFromPayload(full.Payload, "Subject"),
                    full.Snippet));
            }
        }

        return new GmailSearchResults(summaries, result.NextPageToken, result.ResultSizeEstimate ?? 0);
    }

    private static string GetHeaderFromPayload(GmailMessagePayload? payload, string headerName)
    {
        if (payload?.Headers == null) return "";
        return payload.Headers.FirstOrDefault(h => h.Name.Equals(headerName, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    }

    private static string BuildQuery(string? searchQuery, int? maxResults, string? pageToken)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchQuery))
            parts.Add($"q={Uri.EscapeDataString(searchQuery)}");
        if (maxResults.HasValue && maxResults > 0)
            parts.Add($"maxResults={Math.Min(maxResults.Value, 100)}");
        if (!string.IsNullOrWhiteSpace(pageToken))
            parts.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        return string.Join("&", parts);
    }

    private static GmailMessage? ParseMessage(MessageResponse? response)
    {
        if (response == null) return null;

        var payload = response.Payload;
        var isUnread = response.LabelIds?.Contains("UNREAD") ?? false;
        var isSpam = response.LabelIds?.Contains("SPAM") ?? false;
        var isStarred = response.LabelIds?.Contains("STARRED") ?? false;

        var dateStr = "";
        if (payload?.Headers != null)
        {
            dateStr = payload.Headers.FirstOrDefault(h => h.Name.Equals("Date", StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        }

        var receivedAt = DateTimeOffset.TryParse(dateStr, out var dt) ? dt : DateTimeOffset.UtcNow;

        var (text, html) = ExtractContent(payload);

        var labels = response.LabelIds ?? new List<string>();

        return new GmailMessage(
            response.Id,
            response.ThreadId,
            (IReadOnlyList<string>)labels,
            isSpam,
            isStarred,
            isUnread,
            receivedAt,
            response.Snippet,
            payload != null ? ParsePayload(payload) : null,
            text,
            html);
    }

    private static GmailMessagePayload? ParsePayload(MessagePayload? payload)
    {
        if (payload == null) return null;
        return new GmailMessagePayload(
            payload.MimeType,
            payload.Headers?.Select(h => new GmailMessageHeader(h.Name, h.Value)).ToList(),
            payload.Body?.Data,
            payload.Parts?.Select(p => ParseMessagePart(p)).ToList());
    }

    private static GmailMessagePart ParseMessagePart(MessagePayload part)
    {
        var partsList = part.Parts?.Select(p => ParseMessagePart(p)).ToList() ?? new List<GmailMessagePart>();

        return new GmailMessagePart(
            part.MimeType,
            part.Filename,
            part.Body?.Data,
            (IReadOnlyList<GmailMessagePart>)partsList);
    }

    private static (string? text, string? html) ExtractContent(MessagePayload? payload)
    {
        string? text = null;
        string? html = null;

        if (payload?.Body?.Data != null)
        {
            try
            {
                var decoded = DecodeBase64Url(payload.Body.Data);
                if (payload.MimeType?.Contains("text/html") == true)
                    html = decoded;
                else if (payload.MimeType?.Contains("text/plain") == true)
                    text = decoded;
            }
            catch { }
        }

        if (payload?.Parts != null)
        {
            foreach (var part in payload.Parts)
            {
                if (part.MimeType?.Contains("text/plain") == true && part.Body?.Data != null && text == null)
                {
                    try { text = DecodeBase64Url(part.Body.Data); }
                    catch { }
                }
                else if (part.MimeType?.Contains("text/html") == true && part.Body?.Data != null && html == null)
                {
                    try { html = DecodeBase64Url(part.Body.Data); }
                    catch { }
                }
            }
        }

        return (text, html);
    }

    private static string DecodeBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        var bytes = Convert.FromBase64String(base64);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task<GmailResult> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        var token = await oauth.GetAccessTokenAsync(cancellationToken);
        if (token is null) return new GmailResult(false, null, "Gmail is not connected.");

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token expired, try refresh
            token = await oauth.GetAccessTokenAsync(cancellationToken);
            if (token is null) return new GmailResult(false, null, "Gmail authorization expired. Please reconnect.");

            using var retryRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path) { Content = JsonContent.Create(payload) };
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var retryResponse = await httpClient.SendAsync(retryRequest, cancellationToken);
            if (!retryResponse.IsSuccessStatusCode)
            {
                return new GmailResult(false, null, $"Gmail returned {(int)retryResponse.StatusCode}: {await retryResponse.Content.ReadAsStringAsync(cancellationToken)}");
            }

            var retryResult = await retryResponse.Content.ReadFromJsonAsync<GmailIdResponse>(cancellationToken: cancellationToken);
            return new GmailResult(true, retryResult?.Id ?? "", null);
        }

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

    private sealed record Label(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("messagesTotal")] int? MessagesTotal,
        [property: JsonPropertyName("threadsTotal")] int? ThreadsTotal);

    private sealed record LabelsResponse([property: JsonPropertyName("labels")] List<Label>? Labels);

    private sealed record MessageRef(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("threadId")] string ThreadId);

    private sealed record MessagesListResponse(
        [property: JsonPropertyName("messages")] List<MessageRef>? Messages,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken,
        [property: JsonPropertyName("resultSizeEstimate")] int? ResultSizeEstimate);

    private sealed record MessageBody(
        [property: JsonPropertyName("data")] string? Data,
        [property: JsonPropertyName("size")] int? Size);

    private sealed record MessageHeader(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("value")] string Value);

    private sealed record MessagePayload(
        [property: JsonPropertyName("mimeType")] string? MimeType,
        [property: JsonPropertyName("filename")] string? Filename,
        [property: JsonPropertyName("headers")] List<MessageHeader>? Headers,
        [property: JsonPropertyName("body")] MessageBody? Body,
        [property: JsonPropertyName("parts")] List<MessagePayload>? Parts);

    private sealed record MessageResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("threadId")] string ThreadId,
        [property: JsonPropertyName("labelIds")] List<string>? LabelIds,
        [property: JsonPropertyName("snippet")] string? Snippet,
        [property: JsonPropertyName("payload")] MessagePayload? Payload);
}
