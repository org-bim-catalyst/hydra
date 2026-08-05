using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>A candidate returned by a nearest-neighbor vector query, before ranking/enrichment (research.md Decision 3).</summary>
public sealed record VectorSearchCandidate(Guid DocumentChunkId, double Distance);

/// <summary>
/// The vector-store abstraction (docs/ARCHITECTURE.md &#167;13). Two implementations ship as of
/// ADR-0007: SQL Server's native <c>vector</c> column (<c>SqlServerVectorStore</c>, research.md
/// Decision 3) and Pinecone (<c>PineconeVectorStore</c>), selected per knowledge base via
/// <see cref="KnowledgeBase.VectorStoreProvider"/> and resolved through
/// <c>IVectorStoreResolver</c>. Adding a further dedicated vector database later is still an
/// additive <c>Infrastructure</c> implementation plus a DI registration change, never a change to
/// this interface, <c>Application</c>, or <c>Domain</c> (spec.md FR-015, constitution &#167;5).
/// </summary>
public interface IVectorStore
{
    /// <summary>Which <see cref="VectorStoreProvider"/> this implementation backs — lets <c>IVectorStoreResolver</c> pick the right instance.</summary>
    VectorStoreProvider Provider { get; }

    Task UpsertAsync(Guid documentChunkId, Guid embeddingId, Guid knowledgeBaseId, float[] vector, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid documentChunkId, CancellationToken cancellationToken = default);

    /// <summary>Cosine nearest-neighbor search (FR-026), scoped to the given knowledge bases. Callers must ensure every id in <paramref name="knowledgeBaseIds"/> resolves to this store's <see cref="Provider"/> — cross-provider knowledge base sets must be partitioned and merged by the caller (ADR-0007), not passed in mixed.</summary>
    Task<IReadOnlyList<VectorSearchCandidate>> QueryNearestAsync(
        float[] queryVector, IReadOnlyList<Guid> knowledgeBaseIds, int topK, double similarityThreshold,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves an <see cref="IVectorStore"/> by <see cref="VectorStoreProvider"/> value (ADR-0007), mirroring <see cref="IEmbeddingServiceResolver"/>.</summary>
public interface IVectorStoreResolver
{
    IVectorStore Resolve(VectorStoreProvider provider);
}
