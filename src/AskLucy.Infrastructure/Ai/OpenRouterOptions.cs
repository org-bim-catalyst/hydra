namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22).</summary>
public sealed class OpenRouterOptions
{
    public const string SectionName = "OpenRouter";

    public required string ApiKey { get; init; }

    /// <summary>OpenRouter routes by a `"vendor/model"`-prefixed identifier (research.md Decision 1).</summary>
    public string ChatModel { get; init; } = "openai/gpt-4o";

    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";
}
