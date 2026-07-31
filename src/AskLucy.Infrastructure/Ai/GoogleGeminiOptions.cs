namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22).</summary>
public sealed class GoogleGeminiOptions
{
    public const string SectionName = "GoogleGemini";

    public required string ApiKey { get; init; }

    public string ChatModel { get; init; } = "gemini-1.5-pro";

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/";
}
