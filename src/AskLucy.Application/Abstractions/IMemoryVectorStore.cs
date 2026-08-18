namespace AskLucy.Application.Abstractions;

/// <summary>A candidate returned by a nearest-neighbor memory-vector query, before ranking/enrichment (research.md Decision 5).</summary>
public sealed record MemoryVectorSearchCandidate(Guid MemoryId, double Distance);

/// <summary>
/// The memory-vector-store abstraction (research.md Decision 5) — a dedicated interface, not a
/// reuse of <see cref="IVectorStore"/>: that interface's methods are hard-parameterized to
/// <c>documentChunkId</c>/<c>knowledgeBaseId</c>, so a parallel, purpose-built contract is used
/// here instead (see research.md Decision 5 for why generalizing <see cref="IVectorStore"/> was
/// rejected). The single implementation, <c>SqlServerMemoryVectorStore</c>, reuses the same
/// raw-ADO.NET-against-a-<c>vector(n)</c>-column technique <c>SqlServerVectorStore</c> already
/// proved for RAG — a shared implementation *technique*, not a shared table or abstraction.
/// </summary>
public interface IMemoryVectorStore
{
    Task UpsertAsync(Guid memoryId, Guid embeddingId, float[] vector, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid memoryId, CancellationToken cancellationToken = default);

    /// <summary>Cosine nearest-neighbor search scoped to one user's memories, optionally narrowed to a single active Project — a query with <paramref name="projectId"/> set matches memories where <c>ProjectId</c> is either null (general) or equal to <paramref name="projectId"/> (spec.md FR-002, FR-011, User Story 5).</summary>
    Task<IReadOnlyList<MemoryVectorSearchCandidate>> QueryNearestAsync(
        float[] queryVector, string userId, Guid? projectId, int topK, double similarityThreshold,
        CancellationToken cancellationToken = default);
}
