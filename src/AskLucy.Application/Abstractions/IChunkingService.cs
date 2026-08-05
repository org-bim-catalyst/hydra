using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>One chunk produced by a chunking strategy, before persistence (research.md Decision 4).</summary>
public sealed record ChunkCandidate(
    string Content, int TokenCount, int CharacterCount, string? Language, int? PageNumber,
    string? Section, string? Heading, int Position);

/// <summary>
/// One chunking algorithm (spec.md FR-001, docs/ARCHITECTURE.md &#167;13's <c>IChunkingService</c>
/// naming). Selected via the Strategy pattern, keyed by <c>KnowledgeBase.ChunkingStrategy</c>
/// (research.md Decision 4).
/// </summary>
public interface IChunkingStrategy
{
    ChunkingStrategy Strategy { get; }

    Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(
        string extractedText, string? extractedStructureJson, string? language,
        CancellationToken cancellationToken = default);
}

/// <summary>Resolves an <see cref="IChunkingStrategy"/> by <see cref="ChunkingStrategy"/> value.</summary>
public interface IChunkingService
{
    IChunkingStrategy Resolve(ChunkingStrategy strategy);
}
