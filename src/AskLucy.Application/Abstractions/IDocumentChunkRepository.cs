using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="DocumentChunk"/> (constitution §3 Repository rules).</summary>
public interface IDocumentChunkRepository
{
    Task<DocumentChunk?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All chunks belonging to a knowledge base document, in document order (FR-002 Position).</summary>
    Task<IReadOnlyList<DocumentChunk>> GetByKnowledgeBaseDocumentAsync(Guid knowledgeBaseDocumentId, CancellationToken cancellationToken = default);

    /// <summary>FR-005 — used to skip re-embedding when a chunk's content is unchanged.</summary>
    Task<DocumentChunk?> FindByContentHashAsync(Guid knowledgeBaseDocumentId, string contentHash, CancellationToken cancellationToken = default);

    void Add(DocumentChunk chunk);

    void AddRange(IEnumerable<DocumentChunk> chunks);

    /// <summary>FR-016 — soft-deletes every chunk belonging to a knowledge base document (superseded version, deleted/archived document).</summary>
    Task SoftDeleteByKnowledgeBaseDocumentAsync(Guid knowledgeBaseDocumentId, string actor, CancellationToken cancellationToken = default);
}
