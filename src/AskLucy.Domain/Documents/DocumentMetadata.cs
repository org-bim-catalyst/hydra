using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// Structured, editable descriptive fields for a <see cref="Document"/> (FR-023, FR-031,
/// FR-031a, data-model.md). <see cref="IsAutoExtracted"/> starts <c>true</c> and flips
/// permanently <c>false</c> the first time a user edits any field — distinguishing an
/// auto-extracted value from a user override, per FR-023.
/// </summary>
public sealed class DocumentMetadata : BaseEntity
{
    public Guid DocumentId { get; private set; }

    public string? Title { get; private set; }

    public string? Author { get; private set; }

    public DateTime? CreationDate { get; private set; }

    public DateTime? ModificationDate { get; private set; }

    public string? Keywords { get; private set; }

    public string? Encoding { get; private set; }

    public bool IsAutoExtracted { get; private set; } = true;

    private DocumentMetadata()
    {
        // Required by EF Core materialization.
    }

    /// <summary>Populated by the Metadata Extraction processing stage — always auto-extracted at creation.</summary>
    public static DocumentMetadata CreateFromExtraction(
        Guid documentId, string? title, string? author, DateTime? creationDate, DateTime? modificationDate,
        string? keywords, string? encoding, string actor)
    {
        return new DocumentMetadata
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Title = title,
            Author = author,
            CreationDate = creationDate,
            ModificationDate = modificationDate,
            Keywords = keywords,
            Encoding = encoding,
            IsAutoExtracted = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>An independent copy of another document's metadata for <c>DuplicateDocument</c> (FR-034) — preserves <see cref="IsAutoExtracted"/> as-is, since a duplicate of already user-corrected metadata shouldn't revert to looking auto-extracted.</summary>
    public static DocumentMetadata CreateCopy(Guid documentId, DocumentMetadata source, string actor)
    {
        return new DocumentMetadata
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Title = source.Title,
            Author = source.Author,
            CreationDate = source.CreationDate,
            ModificationDate = source.ModificationDate,
            Keywords = source.Keywords,
            Encoding = source.Encoding,
            IsAutoExtracted = source.IsAutoExtracted,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>A user-initiated edit (FR-031) — only supplied fields change; permanently marks this record as no longer purely auto-extracted (FR-023).</summary>
    public void ApplyUserEdit(string? title, string? author, DateTime? creationDate, DateTime? modificationDate, string? keywords, string actor)
    {
        Title = title;
        Author = author;
        CreationDate = creationDate;
        ModificationDate = modificationDate;
        Keywords = keywords;
        IsAutoExtracted = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>
    /// Re-runs of the Metadata Extraction stage (US5 <c>ReplaceDocument</c> reprocessing a new
    /// version) call this instead of <see cref="CreateFromExtraction"/> when a row already exists
    /// for the document (FR-023's unique-per-document constraint). Idempotent no-op if
    /// <see cref="IsAutoExtracted"/> is already <c>false</c> — a user's manual correction (FR-031)
    /// must never be silently clobbered by a fresh automatic extraction from the replacement file.
    /// </summary>
    public void ApplyReExtraction(string? title, string? author, DateTime? creationDate, DateTime? modificationDate, string? keywords, string? encoding, string actor)
    {
        if (!IsAutoExtracted)
        {
            return;
        }

        Title = title;
        Author = author;
        CreationDate = creationDate;
        ModificationDate = modificationDate;
        Keywords = keywords;
        Encoding = encoding;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
