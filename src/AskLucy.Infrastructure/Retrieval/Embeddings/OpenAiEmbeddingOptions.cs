namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22). Reuses the platform's existing OpenAI credential where configured (same vendor as the chat provider), but is a distinct options section since embedding generation is decoupled from chat model selection (spec.md FR-006).</summary>
public sealed class OpenAiEmbeddingOptions
{
    public const string SectionName = "OpenAiEmbedding";

    public required string ApiKey { get; init; }

    public string Model { get; init; } = "text-embedding-3-small";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
}
