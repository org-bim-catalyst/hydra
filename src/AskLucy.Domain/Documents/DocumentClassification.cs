using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

public enum DocumentClassificationSource
{
    Automatic,
    UserOverride,
}

/// <summary>The category assigned to a <see cref="Document"/> (FR-025, FR-026, data-model.md) — a document has exactly one current classification.</summary>
public sealed class DocumentClassification : BaseEntity
{
    public Guid DocumentId { get; private set; }

    public Guid CategoryId { get; private set; }

    public DocumentClassificationSource Source { get; private set; }

    /// <summary>Only populated when <see cref="Source"/> is <see cref="DocumentClassificationSource.Automatic"/>.</summary>
    public decimal? ConfidenceScore { get; private set; }

    private DocumentClassification()
    {
        // Required by EF Core materialization.
    }

    /// <summary>Populated by the Classification processing stage.</summary>
    public static DocumentClassification CreateAutomatic(Guid documentId, Guid categoryId, decimal confidenceScore, string actor)
    {
        return new DocumentClassification
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            CategoryId = categoryId,
            Source = DocumentClassificationSource.Automatic,
            ConfidenceScore = confidenceScore,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>A user-assigned classification for a document that was never automatically classified (e.g. Classification was Skipped for lack of extracted text) — FR-026 still applies even without a prior automatic value to override.</summary>
    public static DocumentClassification CreateUserOverride(Guid documentId, Guid categoryId, string actor)
    {
        return new DocumentClassification
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            CategoryId = categoryId,
            Source = DocumentClassificationSource.UserOverride,
            ConfidenceScore = null,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>An independent copy of another document's classification for <c>DuplicateDocument</c> (FR-034) — preserves <see cref="Source"/>/<see cref="ConfidenceScore"/> as-is.</summary>
    public static DocumentClassification CreateCopy(Guid documentId, DocumentClassification source, string actor)
    {
        return new DocumentClassification
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            CategoryId = source.CategoryId,
            Source = source.Source,
            ConfidenceScore = source.ConfidenceScore,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>A user override (FR-026) — retains the distinction from an automatic classification even after being overridden.</summary>
    public void ApplyOverride(Guid categoryId, string actor)
    {
        CategoryId = categoryId;
        Source = DocumentClassificationSource.UserOverride;
        ConfidenceScore = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>
    /// Re-runs of the Classification stage (US5 <c>ReplaceDocument</c> reprocessing a new version)
    /// call this instead of <see cref="CreateAutomatic"/> when a row already exists for the
    /// document (FR-025's one-classification-per-document constraint). Idempotent no-op when
    /// <see cref="Source"/> is already <see cref="DocumentClassificationSource.UserOverride"/> —
    /// a user's override (FR-026) must never be silently clobbered by a fresh automatic
    /// classification from the replacement file.
    /// </summary>
    public void ApplyAutomaticReclassification(Guid categoryId, decimal confidenceScore, string actor)
    {
        if (Source == DocumentClassificationSource.UserOverride)
        {
            return;
        }

        CategoryId = categoryId;
        ConfidenceScore = confidenceScore;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
