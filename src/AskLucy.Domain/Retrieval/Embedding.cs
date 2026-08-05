using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// A vector representation of a chunk's content produced by a specific embedding provider/model
/// (spec.md FR-006–FR-009a, data-model.md). Immutable after creation — a re-embed always inserts a
/// new row and flips <see cref="IsCurrent"/>, never updates a vector in place, so
/// <see cref="RetrievalHistory"/>/citation rows created against an older embedding remain
/// meaningful for cross-version quality comparison (constitution §5). Collapses spec.md's
/// conceptual <c>ChunkEmbedding</c> entity into this flag (research.md Decision 5, plan.md
/// "Explicitly Not Modeled") — a chunk's embedding history is one-to-many, never many-to-many, so
/// no separate join table carries any independent behavior. The vector itself is a plain
/// <c>float[]</c> here (never an EF Core type) per constitution §3 Domain purity; the
/// Persistence-layer configuration maps it to/from SQL Server's native <c>vector(n)</c> column
/// type (research.md Decision 3).
/// </summary>
public sealed class Embedding : BaseEntity
{
    /// <summary>
    /// The fixed width of the <c>vector(n)</c> SQL Server column every <see cref="Vector"/> is
    /// stored in (research.md Decision 3 implementation note, discovered during
    /// <c>/speckit-implement</c> Foundational work). SQL Server's native vector column type is
    /// fixed-width per column, but this platform supports multiple <see cref="EmbeddingProvider"/>s
    /// with different natural dimensionalities (OpenAI's 1536 vs. a local ONNX model's 384) — since
    /// a search only ever compares vectors from the *same* provider (FR-008 already forbids mixing
    /// providers in one ranked result set), every provider's output is projected/zero-padded to
    /// this single shared width by its <c>IEmbeddingService</c> implementation before storage, so
    /// one physical column serves every provider without per-provider schema variants.
    /// </summary>
    public const int VectorWidth = 1536;

    public Guid DocumentChunkId { get; private set; }

    public Guid EmbeddingProviderId { get; private set; }

    /// <summary>
    /// Populated when this instance was constructed via <see cref="Create"/> or loaded through
    /// <c>IVectorStore</c>. An instance loaded through <c>IEmbeddingRepository</c>/EF Core directly
    /// (which excludes this column from its model — see <c>EmbeddingConfiguration</c>'s remarks)
    /// will have this at its default empty value; callers needing the vector itself must go
    /// through <c>IVectorStore</c>, not the EF-backed repository.
    /// </summary>
    public float[] Vector { get; private set; } = [];

    public bool IsCurrent { get; private set; }

    private Embedding()
    {
        // Required by EF Core materialization.
    }

    public static Embedding Create(Guid documentChunkId, Guid embeddingProviderId, float[] vector, string actor)
    {
        if (vector.Length == 0)
        {
            throw new DomainRuleViolationException("An embedding must have a non-empty vector.");
        }

        return new Embedding
        {
            Id = Guid.CreateVersion7(),
            DocumentChunkId = documentChunkId,
            EmbeddingProviderId = embeddingProviderId,
            Vector = vector,
            IsCurrent = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Called when a newer embedding supersedes this one (re-embed, provider/model change, FR-008) — this row's history is retained, never deleted.</summary>
    public void MarkSuperseded(string actor)
    {
        IsCurrent = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
