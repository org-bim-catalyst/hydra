using AskLucy.Domain.Common;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>Reflects this spec's own lightweight post-upload work (content validation,
/// page-count extraction) — not a RAG-ingestion status; that pipeline is a future spec and
/// out of scope here (data-model.md).</summary>
public enum KnowledgeBaseDocumentProcessingStatus
{
    Uploaded,
    Ready,
    Failed,
}

/// <summary>
/// Associates an uploaded file (via <c>IFileStorage</c>) with exactly one knowledge base and
/// at most one folder (FR-016). New concept — nothing like this existed before this feature
/// (plan.md Summary); it is not part of the future RAG pipeline's text-extraction/embedding
/// work, only the organizational association and cached-statistics contribution.
/// </summary>
public sealed class KnowledgeBaseDocument : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public Guid? FolderId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string StoredFileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public int? PageCount { get; private set; }

    public KnowledgeBaseDocumentProcessingStatus ProcessingStatus { get; private set; }

    public DateTime UploadedAtUtc { get; private set; }

    private KnowledgeBaseDocument()
    {
        // Required by EF Core materialization.
    }

    public static KnowledgeBaseDocument Create(
        Guid knowledgeBaseId, Guid? folderId, string fileName, string storedFileName, string contentType, long sizeBytes, int? pageCount, string actor)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainRuleViolationException("A document file name is required.");
        }

        if (string.IsNullOrWhiteSpace(storedFileName))
        {
            throw new DomainRuleViolationException("A document must have a storage reference.");
        }

        var uploadedAtUtc = DateTime.UtcNow;

        return new KnowledgeBaseDocument
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            FolderId = folderId,
            FileName = fileName,
            StoredFileName = storedFileName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            PageCount = pageCount,
            ProcessingStatus = KnowledgeBaseDocumentProcessingStatus.Ready,
            UploadedAtUtc = uploadedAtUtc,
            CreatedAtUtc = uploadedAtUtc,
            CreatedBy = actor,
        };
    }

    /// <summary>Marks page-count extraction (research.md Decision 5) as having failed — the upload itself already succeeded, so this never throws; it only records that the derived statistic is unavailable.</summary>
    public void MarkProcessingFailed(string actor)
    {
        ProcessingStatus = KnowledgeBaseDocumentProcessingStatus.Failed;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Move(Guid? newFolderId, string actor)
    {
        FolderId = newFolderId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
