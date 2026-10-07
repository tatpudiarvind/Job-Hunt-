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
}

/// <summary>Optional OpenAI chat-completions adapter. Only used when settings select it and an API key exists.</summary>
public sealed class OpenAiLlmProvider(HttpClient httpClient, OpenAiOptions options) : ILlmProvider
{
    public string Name => "OpenAI";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("OpenAI is not configured. Set OpenAI:ApiKey.");

        var messages = new List<object> { new { role = "system", content = request.SystemPrompt } };
        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.BaseUrl), "chat/completions"))
        {
            Content = JsonContent.Create(new
            {
                model = options.Model,
                temperature = request.Temperature,
                response_format = request.JsonSchemaName is null ? null : new { type = "json_object" },
                messages
            }, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull })
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var response = await httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from OpenAI.");
        var content = payload.Choices.FirstOrDefault()?.Message.Content ?? "";
        return new LlmResponse(content, Name, payload.Model ?? options.Model, payload.Usage?.PromptTokens, payload.Usage?.CompletionTokens);
    }

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
