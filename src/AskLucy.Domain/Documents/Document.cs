using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>A new file-type set scoped to this bounded context (research.md Decision 1) — not <c>KnowledgeBaseDocumentType</c>, which is scoped to RAG-ingestible formats only.</summary>
public enum DocumentFileType
{
    Pdf,
    Word,
    Excel,
    PowerPoint,
    Rtf,
    Markdown,
    Html,
    Csv,
    Json,
    Xml,
    Text,
    Png,
    Jpeg,
    Tiff,
    Bmp,
    Webp,
}

/// <summary>The automated pipeline's outcome (FR-012). Orthogonal to <see cref="Document.ArchivedAtUtc"/> and <see cref="BaseEntity.DeletedAtUtc"/> (data-model.md modeling note).</summary>
public enum DocumentProcessingStatus
{
    Uploaded,
    Queued,
    Processing,
    Completed,
    Failed,
}

/// <summary>
/// The aggregate root of the Document Intelligence Pipeline (spec.md FR-001, FR-012–FR-019;
/// data-model.md). A new, independent bounded context from <c>KnowledgeBaseDocument</c>
/// (research.md Decision 1) — this entity's lifecycle (OCR, versioning, classification) is
/// unrelated to knowledge-base membership. <see cref="ArchivedAtUtc"/> and
/// <see cref="BaseEntity.DeletedAtUtc"/> are independent axes from <see cref="ProcessingStatus"/>,
/// mirroring the Archived/Favorite/Pinned-vs-status split already established by
/// <c>KnowledgeBase</c> (specs/014 research.md Decision 2) — a document can be archived without
/// losing its processing outcome, and deleted without first needing to "cancel" a status that
/// doesn't apply to delete.
/// </summary>
public sealed class Document : BaseEntity
{
    private readonly List<DocumentTag> _tags = [];

    public string OwnerId { get; private set; } = string.Empty;

    public Guid? FolderId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public DocumentFileType FileType { get; private set; }

    public long SizeBytes { get; private set; }

    public Guid CurrentVersionId { get; private set; }

    public DocumentProcessingStatus ProcessingStatus { get; private set; }

    /// <summary>Non-null = archived (FR-016). Independent of <see cref="BaseEntity.DeletedAtUtc"/> — a document may be both archived and soft-deleted.</summary>
    public DateTime? ArchivedAtUtc { get; private set; }

    public IReadOnlyCollection<DocumentTag> Tags => _tags;

    private Document()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// <paramref name="id"/> is caller-supplied (not self-generated) because the first
    /// <see cref="DocumentVersion"/> must reference this document's id before this row exists —
    /// the caller generates one id upfront and passes it to both factories, resolving the
    /// Document&lt;-&gt;DocumentVersion circular reference (data-model.md; there is no
    /// DB-enforced FK for <see cref="CurrentVersionId"/>, so this is purely an application-level
    /// ordering concern).
    /// </summary>
    public static Document Create(Guid id, string ownerId, string fileName, DocumentFileType fileType, long sizeBytes, Guid currentVersionId, string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A document must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException("A document file name is required.");
        }

        return new Document
        {
            Id = id,
            OwnerId = ownerId,
            FileName = fileName.Trim(),
            FileType = fileType,
            SizeBytes = sizeBytes,
            CurrentVersionId = currentVersionId,
            ProcessingStatus = DocumentProcessingStatus.Uploaded,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-019 — never touches stored content, version history, or processing state.</summary>
    public void Rename(string fileName, string actor)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException("A document file name is required.");
        }

        FileName = fileName.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-033 — null moves the document to the root level.</summary>
    public void MoveToFolder(Guid? folderId, string actor)
    {
        FolderId = folderId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-016. Idempotent no-op if already archived.</summary>
    public void Archive(string actor)
    {
        if (ArchivedAtUtc is not null)
        {
            return;
        }

        ArchivedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Undoes <see cref="Archive"/> only (FR-016) — distinct from <see cref="Undelete"/>, since the two flags are independent (data-model.md).</summary>
    public void Restore(string actor)
    {
        if (ArchivedAtUtc is null)
        {
            return;
        }

        ArchivedAtUtc = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-017 — recoverable soft delete, never an immediate irreversible removal.</summary>
    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    /// <summary>Undoes <see cref="SoftDelete"/> only — distinct from <see cref="Restore"/>.</summary>
    public void Undelete(string actor)
    {
        if (DeletedAtUtc is null)
        {
            return;
        }

        DeletedAtUtc = null;
        DeletedBy = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Advances the automated pipeline's status (FR-012, FR-020, FR-028, FR-029) — called by the processing pipeline, never directly by a user command.</summary>
    public void SetProcessingStatus(DocumentProcessingStatus status, string actor)
    {
        ProcessingStatus = status;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>Repoints the current version (FR-038 replace, FR-041 restore) without touching version history — the caller is responsible for having already created/located the target <c>DocumentVersion</c>.</summary>
    public void SetCurrentVersion(Guid versionId, long sizeBytes, DocumentFileType fileType, string actor)
    {
        CurrentVersionId = versionId;
        SizeBytes = sizeBytes;
        FileType = fileType;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public bool IsOwnedBy(string userId) => OwnerId == userId;

    /// <summary>FR-032 — <paramref name="tag"/> is a pre-existing, looked-up <see cref="DocumentTag"/> (tags are shared across a user's documents, so a command handler resolves/creates the tag row first, unlike <c>KnowledgeBase.AddTag</c>'s per-instance tags).</summary>
    public void AddTag(DocumentTag tag, string actor)
    {
        if (_tags.Any(t => t.Id == tag.Id))
        {
            return;
        }

        _tags.Add(tag);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void RemoveTag(DocumentTag tag, string actor)
    {
        if (_tags.RemoveAll(t => t.Id == tag.Id) == 0)
        {
            return;
        }

        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
