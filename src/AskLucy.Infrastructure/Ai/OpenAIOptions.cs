namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution &#167;8/&#167;22).</summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public required string ApiKey { get; init; }

    public string ChatModel { get; init; } = "gpt-3.5-turbo";

    public string ImageModel { get; init; } = "dall-e-3";

    public string TranscriptionModel { get; init; } = "whisper-1";

    public string BaseUrl { get; init; } = "https://api.openai.com/v1/";
}
