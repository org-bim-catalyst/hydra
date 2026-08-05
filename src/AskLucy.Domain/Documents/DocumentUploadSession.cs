using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// Not in the original data-model.md — added during US1 implementation. Tracks a resumable
/// chunked upload in progress (FR-005, research.md Decision 6) between the client's
/// <c>StartUpload</c>/<c>UploadChunk</c>/<c>CompleteUpload</c> calls. The accumulated chunk
/// bytes themselves live in <c>IResumableUploadStorage</c> (a temp-storage abstraction, distinct
/// from <c>IFileStorage</c>'s permanent store) — this row only tracks metadata and, once a
/// checksum duplicate is detected at completion, the already-finalized permanent file/hash
/// pending the caller's version-vs-new-document choice (FR-009).
/// </summary>
public enum DocumentUploadSessionStatus
{
    InProgress,

    /// <summary>A checksum match was found at completion (FR-009); the final file is already saved to permanent storage, awaiting <c>CompleteUploadAsVersion</c>/<c>CompleteUploadAsNew</c>.</summary>
    PendingDuplicateResolution,

    Completed,
    Cancelled,
}

public sealed class DocumentUploadSession : BaseEntity
{
    public string OwnerId { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;

    public long DeclaredSizeBytes { get; private set; }

    public long ChunkSizeBytes { get; private set; }

    public DocumentUploadSessionStatus Status { get; private set; }

    public string? PendingStoredFileName { get; private set; }

    public string? PendingChecksumHash { get; private set; }

    /// <summary>
    /// Non-null only for a US5 replace-version upload (FR-038) — set at <c>StartUpload</c> time so
    /// <c>RestoreDocumentVersion</c>'s in-flight conflict check (Edge Cases,
    /// contracts/document-versions-folders-api.md) can find it before the upload finishes. A
    /// plain new-document upload never sets this.
    /// </summary>
    public Guid? TargetDocumentId { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    private DocumentUploadSession()
    {
        // Required by EF Core materialization.
    }

    public static DocumentUploadSession Create(
        string ownerId, string fileName, long declaredSizeBytes, long chunkSizeBytes, DateTime expiresAtUtc, string actor, Guid? targetDocumentId = null)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("An upload session must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException("A file name is required.");
        }

        return new DocumentUploadSession
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            FileName = fileName.Trim(),
            DeclaredSizeBytes = declaredSizeBytes,
            ChunkSizeBytes = chunkSizeBytes,
            Status = DocumentUploadSessionStatus.InProgress,
            TargetDocumentId = targetDocumentId,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void EnsureInProgress()
    {
        if (Status != DocumentUploadSessionStatus.InProgress)
        {
            throw new DomainRuleViolationException("This upload session is no longer in progress.");
        }
    }

    /// <summary>FR-009 — a checksum match was found; the final file is already persisted, this session now just remembers where until the caller resolves it.</summary>
    public void MarkPendingDuplicateResolution(string storedFileName, string checksumHash, string actor)
    {
        EnsureInProgress();
        Status = DocumentUploadSessionStatus.PendingDuplicateResolution;
        PendingStoredFileName = storedFileName;
        PendingChecksumHash = checksumHash;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Complete(string actor)
    {
        Status = DocumentUploadSessionStatus.Completed;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Cancel(string actor)
    {
        Status = DocumentUploadSessionStatus.Cancelled;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public bool IsOwnedBy(string userId) => OwnerId == userId;
}
