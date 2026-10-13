using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArvindJobHunter.Agents.Llm;

public sealed class OpenAiOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-4o-mini";
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string DisplayName { get; init; } = "OpenAI";
}

/// <summary>Optional OpenAI-compatible chat-completions adapter. Can target OpenAI, proxies, and local/self-hosted endpoints with the same API shape.</summary>
public sealed class OpenAiLlmProvider(HttpClient httpClient, IRuntimeSettingsProvider settings, OpenAiOptions options, ILogger<OpenAiLlmProvider> logger) : ILlmProvider
{
    private const int MaxErrorDetailLength = 500;

    public string Name => "OpenAI";
    public bool IsConfigured => true;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var resolved = Resolve(current);
        if (string.IsNullOrWhiteSpace(resolved.ApiKey)) throw new InvalidOperationException("The selected LLM is not configured. Set an API key in Settings.");

        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var endpoint = new Uri(new Uri(resolved.BaseUrl), "chat/completions");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = resolved.Model,
                temperature = request.Temperature,
                response_format = request.JsonSchemaName is null ? null : new { type = "json_object" },
                messages
            }, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resolved.ApiKey);

        var schema = request.JsonSchemaName ?? "text";
        logger.LogDebug("LLM request to {Endpoint}: model {Model}, schema {Schema}, {PromptCharacters} prompt characters",
            endpoint, resolved.Model, schema, request.SystemPrompt.Length + request.Messages.Sum(m => m.Content.Length));
        var watch = Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ErrorDetail(await response.Content.ReadAsStringAsync(cancellationToken));
            logger.LogError("LLM call to {Endpoint} (model {Model}, schema {Schema}) failed with {StatusCode} {Reason} after {ElapsedMs} ms: {Detail}",
                endpoint, resolved.Model, schema, (int)response.StatusCode, response.ReasonPhrase, watch.ElapsedMilliseconds, detail);
            throw new HttpRequestException($"{resolved.DisplayName} returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}", null, response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from the configured LLM provider.");
        var content = payload.Choices.FirstOrDefault()?.Message.Content ?? "";
        logger.LogInformation("LLM {Provider} ({Model}) answered {Schema} in {ElapsedMs} ms · tokens {PromptTokens} prompt + {CompletionTokens} completion",
            resolved.DisplayName, payload.Model ?? resolved.Model, schema, watch.ElapsedMilliseconds,
            payload.Usage?.PromptTokens.ToString() ?? "?", payload.Usage?.CompletionTokens.ToString() ?? "?");
        if (content.Length == 0) logger.LogWarning("LLM {Provider} returned an empty message for {Schema}", resolved.DisplayName, schema);
        return new LlmResponse(content, resolved.DisplayName, payload.Model ?? resolved.Model, payload.Usage?.PromptTokens, payload.Usage?.CompletionTokens);
    }

    public bool CanResolve(RuntimeSettings settings) =>
        string.Equals(settings.LlmProvider, Name, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Resolve(settings).ApiKey);

    public string DisplayName(RuntimeSettings settings) => Resolve(settings).DisplayName;

    /// <summary>OpenAI-style APIs explain failures in <c>{"error":{"message":…}}</c>; keep that text so the user sees why a run failed.</summary>
    private static string ErrorDetail(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String) return Truncate(text.GetString()!);
                if (error.ValueKind == JsonValueKind.String) return Truncate(error.GetString()!);
            }
        }
        catch (JsonException)
        {
            // not JSON; fall through to the raw body
        }

        return string.IsNullOrWhiteSpace(body) ? "(empty response body)" : Truncate(body.Trim());
    }

    private static string Truncate(string value) => value.Length <= MaxErrorDetailLength ? value : value[..MaxErrorDetailLength] + "…";

    private OpenAiOptions Resolve(RuntimeSettings settings) => new()
    {
        ApiKey = string.IsNullOrWhiteSpace(settings.LlmApiKey) ? options.ApiKey : settings.LlmApiKey,
        Model = string.IsNullOrWhiteSpace(settings.LlmModel) ? options.Model : settings.LlmModel,
        BaseUrl = string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ? options.BaseUrl : EnsureTrailingSlash(settings.LlmBaseUrl),
        DisplayName = string.IsNullOrWhiteSpace(settings.LlmDisplayName) ? options.DisplayName : settings.LlmDisplayName
    };

    private static string EnsureTrailingSlash(string value) => value.EndsWith('/') ? value : value + "/";

    private sealed record ChatCompletionResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] List<Choice> Choices,
        [property: JsonPropertyName("usage")] Usage? Usage);

    private sealed record Choice([property: JsonPropertyName("message")] ChoiceMessage Message);

    private sealed record ChoiceMessage([property: JsonPropertyName("content")] string? Content);

    private sealed record Usage(
        [property: JsonPropertyName("prompt_tokens")] int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens);
}
