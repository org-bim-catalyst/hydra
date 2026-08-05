using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

public enum IndexingStage
{
    Chunking,
    EmbeddingGeneration,
    VectorWrite,
    Cleanup,
}

public enum IndexingStageStatus
{
    Started,
    Completed,
    Failed,
}

/// <summary>
/// A timestamped record of a stage transition or event within an <see cref="IndexingJob"/>
/// (spec.md FR-039, data-model.md). Append-only — no soft delete, mirroring
/// <c>DocumentProcessingLog</c>'s documented exception (specs/015).
/// </summary>
public sealed class IndexingLog : BaseEntity
{
    public Guid IndexingJobId { get; private set; }

    public IndexingStage Stage { get; private set; }

    public IndexingStageStatus Status { get; private set; }

    public string? Message { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private IndexingLog()
    {
        // Required by EF Core materialization.
    }

    public static IndexingLog Create(Guid indexingJobId, IndexingStage stage, IndexingStageStatus status, string? message, string actor)
    {
        var occurredAtUtc = DateTime.UtcNow;

        return new IndexingLog
        {
            Id = Guid.CreateVersion7(),
            IndexingJobId = indexingJobId,
            Stage = stage,
            Status = status,
            Message = message,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = occurredAtUtc,
            CreatedBy = actor,
        };
    }
}
