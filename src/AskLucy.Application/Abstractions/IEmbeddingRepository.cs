using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Repository for <see cref="Embedding"/> (constitution §3 Repository rules). Manages the
/// EF-mapped metadata columns only — <see cref="Embedding.Vector"/> itself is excluded from the EF
/// model (see <c>EmbeddingConfiguration</c>'s remarks) and is read/written directly by
/// <c>IVectorStore</c>'s implementation via raw SQL.
/// </summary>
public interface IEmbeddingRepository
{
    /// <summary>The current (non-superseded) embedding for a chunk, if one exists (FR-008).</summary>
    Task<Embedding?> GetCurrentAsync(Guid documentChunkId, CancellationToken cancellationToken = default);

    void Add(Embedding embedding);

    /// <summary>FR-008 — marks the chunk's existing current embedding (if any) as superseded, so exactly one <c>IsCurrent</c> row exists per chunk once the new one is added.</summary>
    Task MarkExistingSupersededAsync(Guid documentChunkId, string actor, CancellationToken cancellationToken = default);
}
