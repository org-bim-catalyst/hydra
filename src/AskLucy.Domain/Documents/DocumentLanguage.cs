using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

public enum DocumentLanguageRole
{
    Primary,
    Secondary,
}

/// <summary>A detected language for a <see cref="Document"/> (FR-024, data-model.md). Populated by the Language Detection processing stage.</summary>
public sealed class DocumentLanguage : BaseEntity
{
    public Guid DocumentId { get; private set; }

    /// <summary>ISO 639-1 (e.g. "en", "ar").</summary>
    public string LanguageCode { get; private set; } = string.Empty;

    public DocumentLanguageRole Role { get; private set; }

    public decimal ConfidenceScore { get; private set; }

    private DocumentLanguage()
    {
        // Required by EF Core materialization.
    }

    public static DocumentLanguage Create(Guid documentId, string languageCode, DocumentLanguageRole role, decimal confidenceScore, string actor)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            throw new DomainRuleViolationException("A language code is required.");
        }

        return new DocumentLanguage
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            LanguageCode = languageCode,
            Role = role,
            ConfidenceScore = confidenceScore,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
