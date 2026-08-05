using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

public sealed record DetectedLanguage(string LanguageCode, DocumentLanguageRole Role, decimal ConfidenceScore);

/// <summary><see cref="CategoryName"/> is resolved back to a <c>DocumentCategory.Id</c> by the calling stage handler — this abstraction stays free of a repository dependency.</summary>
public sealed record DocumentClassificationResult(string CategoryName, decimal ConfidenceScore);

public sealed record DocumentLanguageAndClassificationResult(IReadOnlyList<DetectedLanguage> Languages, DocumentClassificationResult Classification);

/// <summary>
/// Language detection (FR-024) and classification (FR-025) via one call to the existing
/// multi-provider AI Provider Engine (research.md Decision 4) — no dedicated NLP/ML library is
/// introduced; this reuses the platform's existing <c>IAIProvider</c>/<c>IAIProviderResolver</c>
/// abstraction with a versioned system prompt (constitution §9).
/// </summary>
public interface IDocumentLanguageAndClassifier
{
    Task<DocumentLanguageAndClassificationResult> AnalyzeAsync(
        string extractedText, IReadOnlyList<string> availableCategoryNames, CancellationToken cancellationToken = default);
}
