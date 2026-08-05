using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-024, FR-025 — a single <see cref="IDocumentLanguageAndClassifier"/> call covers both
/// classification and language detection (research.md Decision 4), so this stage persists
/// <see cref="DocumentClassification"/> AND <see cref="DocumentLanguage"/> rows together, and
/// <see cref="LanguageDetectionStageHandler"/> is a documented no-op that skips (its work was
/// already done here) rather than doubling the AI call cost across two stages.
/// </summary>
public sealed class ClassificationStageHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    IDocumentLanguageAndClassifier classifier) : IProcessingStageHandler
{
    public DocumentProcessingStageType StageType => DocumentProcessingStageType.Classification;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        var extractedText = version.ExtractedText ?? version.OcrTextRaw;
        if (string.IsNullOrWhiteSpace(extractedText))
        {
            return ProcessingStageOutcome.Skipped; // Nothing to classify/detect language from.
        }

        var categories = await documentRepository.ListCategoriesAsync(cancellationToken);
        var categoryNames = categories.Select(c => c.Name).ToList();

        var result = await classifier.AnalyzeAsync(extractedText, categoryNames, cancellationToken);

        var categoryId = categories.First(c => string.Equals(c.Name, result.Classification.CategoryName, StringComparison.OrdinalIgnoreCase)).Id;

        // Reprocessing a replaced version (US5) must not violate DocumentClassification's
        // one-row-per-document constraint — update the existing row (unless a user already
        // overrode it) instead of blindly adding a second one.
        var existingClassification = await documentRepository.GetClassificationByDocumentIdAsync(documentId, cancellationToken);
        if (existingClassification is not null)
        {
            existingClassification.ApplyAutomaticReclassification(categoryId, result.Classification.ConfidenceScore, "system:processing");
        }
        else
        {
            documentRepository.AddClassification(
                DocumentClassification.CreateAutomatic(documentId, categoryId, result.Classification.ConfidenceScore, "system:processing"));
        }

        // DocumentLanguage has no such constraint (Primary + Secondary rows are expected), but a
        // reprocessing run must still replace the previous set rather than accumulating a stale
        // one alongside the fresh result.
        var existingLanguages = await documentRepository.GetLanguagesByDocumentIdAsync(documentId, cancellationToken);
        if (existingLanguages.Count > 0)
        {
            documentRepository.RemoveLanguages(existingLanguages);
        }

        foreach (var language in result.Languages)
        {
            documentRepository.AddLanguage(
                DocumentLanguage.Create(documentId, language.LanguageCode, language.Role, language.ConfidenceScore, "system:processing"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ProcessingStageOutcome.Completed;
    }
}
