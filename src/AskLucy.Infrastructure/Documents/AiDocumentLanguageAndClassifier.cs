using System.Text.Json;
using System.Text.Json.Serialization;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Documents;

internal static partial class AiDocumentLanguageAndClassifierLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "AI language/classification response was not valid JSON — falling back to a low-confidence default")]
    public static partial void ResponseParseFailed(ILogger logger, Exception exception);
}

/// <summary>
/// <see cref="IDocumentLanguageAndClassifier"/> implementation — reuses the existing
/// multi-provider AI Provider Engine (FR-024, FR-025, research.md Decision 4) via a single,
/// versioned, non-streaming prompt (constitution §9's justified exception for background batch
/// classification).
/// </summary>
public sealed partial class AiDocumentLanguageAndClassifier(
    DefaultProviderResolver defaultProviderResolver,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    ILogger<AiDocumentLanguageAndClassifier> logger) : IDocumentLanguageAndClassifier
{
    /// <summary>v1 — versioned per constitution §9 ("Prompt engineering... reviewed like code, testable in isolation").</summary>
    private const string SystemPromptV1 =
        "You are a document analysis assistant. Given the extracted text of a document, respond " +
        "with ONLY a single JSON object (no markdown, no commentary) matching exactly this shape: " +
        "{\"primaryLanguage\":\"<ISO 639-1 code>\",\"secondaryLanguages\":[{\"code\":\"<ISO 639-1 code>\",\"confidence\":<0..1>}]," +
        "\"category\":\"<one of the provided category names, verbatim>\",\"categoryConfidence\":<0..1>}. " +
        "Pick the single best-fitting category from the provided list only — never invent a new one.";

    private sealed record ClassificationResponse(
        [property: JsonPropertyName("primaryLanguage")] string PrimaryLanguage,
        [property: JsonPropertyName("secondaryLanguages")] List<SecondaryLanguageResponse>? SecondaryLanguages,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("categoryConfidence")] decimal CategoryConfidence);

    private sealed record SecondaryLanguageResponse(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("confidence")] decimal Confidence);

    public async Task<DocumentLanguageAndClassificationResult> AnalyzeAsync(
        string extractedText, IReadOnlyList<string> availableCategoryNames, CancellationToken cancellationToken = default)
    {
        var resolved = await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        var provider = await providerRepository.GetByIdAsync(resolved.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var model = await modelRepository.GetByIdAsync(resolved.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var aiProvider = providerResolver.Resolve(provider.ProviderKey);
        var modelKey = model.ModelKey;

        var userPrompt = $"Available categories: {string.Join(", ", availableCategoryNames)}\n\n" +
            $"Document text (truncated to the first 8000 characters):\n{Truncate(extractedText, 8000)}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPromptV1),
            new(ChatRole.User, userPrompt),
        };

        var completion = await aiProvider.ChatAsync(messages, modelKey, parameters: null, cancellationToken);

        return ParseResponse(completion.Content, availableCategoryNames);
    }

    private DocumentLanguageAndClassificationResult ParseResponse(string content, IReadOnlyList<string> availableCategoryNames)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ClassificationResponse>(ExtractJsonObject(content))
                ?? throw new JsonException("Empty response.");

            var languages = new List<DetectedLanguage> { new(parsed.PrimaryLanguage, DocumentLanguageRole.Primary, 1.0m) };
            languages.AddRange((parsed.SecondaryLanguages ?? [])
                .Select(l => new DetectedLanguage(l.Code, DocumentLanguageRole.Secondary, l.Confidence)));

            var category = availableCategoryNames.Contains(parsed.Category, StringComparer.OrdinalIgnoreCase)
                ? parsed.Category
                : availableCategoryNames[0];

            return new DocumentLanguageAndClassificationResult(languages, new DocumentClassificationResult(category, parsed.CategoryConfidence));
        }
        catch (JsonException ex)
        {
            AiDocumentLanguageAndClassifierLog.ResponseParseFailed(logger, ex);
            return new DocumentLanguageAndClassificationResult(
                [new DetectedLanguage("en", DocumentLanguageRole.Primary, 0.1m)],
                new DocumentClassificationResult(availableCategoryNames[0], 0.1m));
        }
    }

    /// <summary>Some providers wrap the JSON in prose or a markdown code fence despite instructions — take the outermost {...} span defensively.</summary>
    private static string ExtractJsonObject(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : content;
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}
