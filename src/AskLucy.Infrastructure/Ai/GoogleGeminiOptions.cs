namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment — never hardcoded (constitution §8/§22).</summary>
public sealed class GoogleGeminiOptions
{
    public const string SectionName = "GoogleGemini";

    public required string ApiKey { get; init; }

    public string ChatModel { get; init; } = "gemini-1.5-pro";

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>
    /// specs/042-site-boundary-resolution — the vision-capable model used by
    /// <c>GeminiBoundaryVisionAnalyzer</c>, matching the reference notebook's own
    /// <c>GEMINI_MODEL_NAME</c> constant exactly.
    /// </summary>
    public string VisionModel { get; init; } = "gemini-3.6-flash";
}
