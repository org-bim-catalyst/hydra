using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>Resolves an <see cref="IChunkingStrategy"/> by <see cref="ChunkingStrategy"/> value (research.md Decision 4).</summary>
public sealed class ChunkingService(IEnumerable<IChunkingStrategy> strategies) : IChunkingService
{
    public IChunkingStrategy Resolve(ChunkingStrategy strategy)
    {
        var match = strategies.FirstOrDefault(s => s.Strategy == strategy);
        return match ?? throw new InvalidOperationException($"No chunking strategy registered for '{strategy}'.");
    }
}
