using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>FR-001 — splits into sentences via punctuation-boundary detection, grouping sentences up to the target chunk size (research.md Decision 4).</summary>
public sealed partial class SentenceChunkingStrategy : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Sentence;

    public Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var sentences = SentenceBoundaryRegex()
            .Split(extractedText)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var grouped = ChunkTextHelpers.GroupUnitsIntoChunks(sentences, ChunkTextHelpers.DefaultTargetCharacterSize);

        var candidates = grouped
            .Select((content, index) => ChunkTextHelpers.BuildCandidate(content, language, pageNumber: null, section: null, heading: null, position: index))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkCandidate>>(candidates);
    }

    /// <summary>Splits after '.', '!', or '?' followed by whitespace and an uppercase letter/digit — a simple heuristic, not a full NLP sentence tokenizer (constitution §2.III, no new dependency for this).</summary>
    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9])")]
    private static partial Regex SentenceBoundaryRegex();
}
