using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArvindJobHunter.Application.Abstractions;

namespace ArvindJobHunter.Agents.Llm;

public sealed class OpenAiOptions
{
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "gpt-4o-mini";
    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
    public string DisplayName { get; init; } = "OpenAI";
}

/// <summary>Optional OpenAI-compatible chat-completions adapter. Can target OpenAI, proxies, and local/self-hosted endpoints with the same API shape.</summary>
public sealed class OpenAiLlmProvider(HttpClient httpClient, IRuntimeSettingsProvider settings, OpenAiOptions options) : ILlmProvider
{
    public string Name => "OpenAI";
    public bool IsConfigured => true;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        var current = await settings.GetAsync(cancellationToken);
        var resolved = Resolve(current);
        if (string.IsNullOrWhiteSpace(resolved.ApiKey)) throw new InvalidOperationException("The selected LLM is not configured. Set an API key in Settings.");

        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(resolved.BaseUrl), "chat/completions"))
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

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from the configured LLM provider.");
        var content = payload.Choices.FirstOrDefault()?.Message.Content ?? "";
        return new LlmResponse(content, resolved.DisplayName, payload.Model ?? resolved.Model, payload.Usage?.PromptTokens, payload.Usage?.CompletionTokens);
    }

    public bool CanResolve(RuntimeSettings settings) =>
        string.Equals(settings.LlmProvider, Name, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Resolve(settings).ApiKey);

    public string DisplayName(RuntimeSettings settings) => Resolve(settings).DisplayName;

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
