using AskLucy.Domain.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="MemoryEmbedding"/> metadata rows — mirrors <see cref="IEmbeddingRepository"/>'s shape (the vector itself is managed separately via <see cref="IMemoryVectorStore"/>, research.md Decision 5).</summary>
public interface IMemoryEmbeddingRepository
{
    Task<MemoryEmbedding?> GetCurrentByMemoryIdAsync(Guid memoryId, CancellationToken cancellationToken = default);

    void Add(MemoryEmbedding embedding);
}
