using AskLucy.Application.Documents;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="Document"/> (constitution §3 Repository rules).</summary>
public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Bypasses the soft-delete query filter — needed by restore/undelete flows acting on an already-deleted document.</summary>
    Task<Document?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Document document);

    /// <summary>Duplicate-detection lookup (FR-009) — the id of an existing document owned by <paramref name="ownerId"/> whose current or any prior version has this checksum, or null if none.</summary>
    Task<Guid?> FindDocumentIdByChecksumAsync(string ownerId, string hash, CancellationToken cancellationToken = default);

    /// <summary><see cref="DocumentVersion"/> and <see cref="DocumentChecksum"/> are child rows of the Document aggregate (data-model.md) — added through this same repository rather than their own, since a version/checksum never exists independently of a document.</summary>
    void AddVersion(DocumentVersion version);

    void AddChecksum(DocumentChecksum checksum);

    Task<DocumentVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>Every version of a document, newest-first (FR-040's version timeline).</summary>
    Task<IReadOnlyList<DocumentVersion>> GetVersionsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>The hash string for an existing checksum row, for <c>DuplicateDocument</c> (FR-034) — avoids recomputing SHA-256 over a byte-for-byte copy of already-hashed content.</summary>
    Task<string?> GetChecksumHashAsync(Guid checksumId, CancellationToken cancellationToken = default);

    /// <summary><see cref="DocumentMetadata"/>/<see cref="DocumentLanguage"/>/<see cref="DocumentClassification"/>/<see cref="DocumentPreview"/> are likewise child rows of the Document/DocumentVersion aggregate — added and queried through this same repository.</summary>
    void AddMetadata(DocumentMetadata metadata);

    Task<DocumentMetadata?> GetMetadataByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    void AddLanguage(DocumentLanguage language);

    Task<IReadOnlyList<DocumentLanguage>> GetLanguagesByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Clears the previous language set before a reprocessing run re-adds a fresh one (US5 <c>ReplaceDocument</c>) — unlike <see cref="DocumentMetadata"/>/<see cref="DocumentClassification"/>, <see cref="DocumentLanguage"/> has no unique-per-document constraint, so a stale set would otherwise just keep accumulating.</summary>
    void RemoveLanguages(IEnumerable<DocumentLanguage> languages);

    void AddClassification(DocumentClassification classification);

    Task<DocumentClassification?> GetClassificationByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    void AddPreview(DocumentPreview preview);

    Task<IReadOnlyList<DocumentPreview>> GetPreviewsByVersionIdAsync(Guid documentVersionId, CancellationToken cancellationToken = default);

    /// <summary>US7 — the preview-content streaming endpoint looks up a single preview by id to resolve its <c>StoredFileName</c>.</summary>
    Task<DocumentPreview?> GetPreviewByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentCategory>> ListCategoriesAsync(CancellationToken cancellationToken = default);

    Task<DocumentCategory?> GetCategoryByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a <see cref="DocumentMetadata"/> edit already applied in-memory, using
    /// <paramref name="clientRowVersion"/> — the value the client last read — as the
    /// optimistic-concurrency check (FR-031a, research.md Decision 9). If another edit committed
    /// first, reloads the current row, re-applies <paramref name="reapplyEdit"/> on top of the
    /// latest state, and saves again — returning <c>true</c> (never throwing) so the caller
    /// surfaces a "your view was out of date" warning instead of rejecting the write. Owns its
    /// own save (unlike the rest of the codebase's one-<see cref="IUnitOfWork.SaveChangesAsync"/>-
    /// per-handler convention) because the potential retry is intrinsic to this operation, not an
    /// extra step a caller could reasonably sequence itself.
    /// </summary>
    Task<bool> SaveMetadataResolvingStalenessAsync(
        DocumentMetadata metadata, byte[] clientRowVersion, Action<DocumentMetadata> reapplyEdit, CancellationToken cancellationToken = default);

    /// <summary>Looks up an existing tag by exact name for this owner — tags are shared/reused across a user's documents (data-model.md), so adding one first checks for a match rather than always creating.</summary>
    Task<DocumentTag?> FindTagByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default);

    void AddTag(DocumentTag tag);

    Task<IReadOnlyList<DocumentTag>> ListTagsByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search over the caller's own documents (view + folder + the combined filter set of
    /// FR-035–FR-037, all optional and ANDed together). Cursor-paginated, ordered newest-first
    /// (constitution §6).
    /// </summary>
    Task<(IReadOnlyList<Document> Items, string? NextCursor)> SearchAsync(
        string ownerId, DocumentListView view, Guid? folderId, DocumentSearchFilters filters, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The (non-deleted) documents directly inside a folder — used by <c>DeleteFolder</c>'s <c>onContainedDocuments</c> handling (FR-033, Edge Cases).</summary>
    Task<IReadOnlyList<Document>> ListByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);

    /// <summary>Non-deleted document counts per folder for this owner, for <c>GetFolderTree</c> (FR-033) — a folder with no entry here has zero documents.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountDocumentsByFolderAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Single-folder count, for <c>CreateFolder</c>/<c>RenameFolder</c>/<c>MoveFolder</c> responses — <see cref="CountDocumentsByFolderAsync"/> is the bulk equivalent for <c>GetFolderTree</c>.</summary>
    Task<int> CountDocumentsInFolderAsync(Guid folderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// FR-035–FR-037's combined, optional search filters — all supplied values are ANDed together.
/// <see cref="Query"/> matches filename, extracted text, or metadata title/keywords (FR-035);
/// the rest filter by author/language/tag/category/date-range/status (FR-036).
/// </summary>
public sealed record DocumentSearchFilters(
    string? Query = null,
    string? Author = null,
    string? LanguageCode = null,
    string? Tag = null,
    Guid? CategoryId = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    DocumentProcessingStatus? Status = null)
{
    public static readonly DocumentSearchFilters None = new();
}
