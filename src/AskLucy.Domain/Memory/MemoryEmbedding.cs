using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>
/// A vector representation of a memory's content (spec.md FR-010, FR-011; research.md Decision 5).
/// Structurally parallel to <c>Retrieval.Embedding</c> but a distinct table, not a reuse — that
/// entity is hard-coupled to <c>DocumentChunkId</c> (research.md Decision 5 explains why reuse was
/// rejected). Immutable after creation — a content edit re-embeds and flips
/// <see cref="IsCurrent"/>, mirroring <c>Retrieval.Embedding</c>'s convention exactly.
/// </summary>
public sealed class MemoryEmbedding : BaseEntity
{
    /// <summary>Same fixed column width as <c>Retrieval.Embedding.VectorWidth</c> — one physical <c>vector(n)</c> column serves every provider (research.md Decision 5).</summary>
    public const int VectorWidth = 1536;

    public Guid MemoryId { get; private set; }

    public Guid EmbeddingProviderId { get; private set; }

    /// <summary>Populated only when constructed via <see cref="Create"/> or loaded through <c>IMemoryVectorStore</c> — an instance loaded through EF Core directly (which excludes this column, see <c>MemoryEmbeddingConfiguration</c>) has this at its default empty value.</summary>
    public float[] Vector { get; private set; } = [];

    public bool IsCurrent { get; private set; }

    private MemoryEmbedding()
    {
        // Required by EF Core materialization.
    }

    public static MemoryEmbedding Create(Guid memoryId, Guid embeddingProviderId, float[] vector, string actor)
    {
        if (vector.Length == 0)
        {
            throw new DomainRuleViolationException("A memory embedding must have a non-empty vector.");
        }

        return new MemoryEmbedding
        {
            Id = Guid.CreateVersion7(),
            MemoryId = memoryId,
            EmbeddingProviderId = embeddingProviderId,
            Vector = vector,
            IsCurrent = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void MarkSuperseded(string actor)
    {
        IsCurrent = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
