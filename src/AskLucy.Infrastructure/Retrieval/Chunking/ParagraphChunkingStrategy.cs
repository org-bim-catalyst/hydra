using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>FR-001 — splits on blank-line-separated paragraphs, grouping adjacent short paragraphs up to the target chunk size (research.md Decision 4).</summary>
public sealed class ParagraphChunkingStrategy : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Paragraph;

    public Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var paragraphs = extractedText
            .Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();

        var grouped = ChunkTextHelpers.GroupUnitsIntoChunks(paragraphs, ChunkTextHelpers.DefaultTargetCharacterSize);

        var candidates = grouped
            .Select((content, index) => ChunkTextHelpers.BuildCandidate(content, language, pageNumber: null, section: null, heading: null, position: index))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkCandidate>>(candidates);
    }
}
