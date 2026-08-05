using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// Periodically computed, denormalized aggregate chunk/embedding/storage metrics for a knowledge
/// base (spec.md FR-041, data-model.md) — mirrors <c>DocumentStatistics</c>'s "explicitly not
/// real-time-updated" precedent (specs/015).
/// </summary>
public sealed class ChunkStatistics : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public int TotalChunks { get; private set; }

    public int TotalEmbeddings { get; private set; }

    public long StorageBytes { get; private set; }

    public DateTime ComputedAtUtc { get; private set; }

    private ChunkStatistics()
    {
        // Required by EF Core materialization.
    }

    public static ChunkStatistics Create(Guid knowledgeBaseId, int totalChunks, int totalEmbeddings, long storageBytes, string actor)
    {
        var computedAtUtc = DateTime.UtcNow;

        return new ChunkStatistics
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            TotalChunks = totalChunks,
            TotalEmbeddings = totalEmbeddings,
            StorageBytes = storageBytes,
            ComputedAtUtc = computedAtUtc,
            CreatedAtUtc = computedAtUtc,
            CreatedBy = actor,
        };
    }

    public void Refresh(int totalChunks, int totalEmbeddings, long storageBytes, string actor)
    {
        TotalChunks = totalChunks;
        TotalEmbeddings = totalEmbeddings;
        StorageBytes = storageBytes;
        ComputedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
