namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22).</summary>
public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public required string ApiKey { get; init; }

    public string ChatModel { get; init; } = "claude-3-5-sonnet-20241022";

    public string BaseUrl { get; init; } = "https://api.anthropic.com/v1/";

    /// <summary>Anthropic requires this header on every request (research.md Decision 1).</summary>
    public string ApiVersion { get; init; } = "2023-06-01";
}
