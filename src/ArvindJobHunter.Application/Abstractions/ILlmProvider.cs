namespace ArvindJobHunter.Application.Abstractions;

public sealed record LlmMessage(string Role, string Content);

public sealed record LlmRequest(string SystemPrompt, IReadOnlyList<LlmMessage> Messages, string? JsonSchemaName = null, double Temperature = 0.2);

public sealed record LlmResponse(string Content, string Provider, string Model, int? PromptTokens = null, int? CompletionTokens = null);

/// <summary>Text generation boundary. Implementations must treat external content as untrusted data, not instructions.</summary>
public interface ILlmProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

public interface ILlmProviderResolver
{
    Task<ILlmProvider> ResolveAsync(CancellationToken cancellationToken);
}
