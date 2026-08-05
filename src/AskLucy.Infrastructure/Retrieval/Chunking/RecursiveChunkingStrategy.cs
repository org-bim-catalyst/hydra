using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>
/// FR-001 — recursively splits text using a hierarchy of separators (paragraph → line →
/// sentence → word), descending only where a unit still exceeds the target size, so chunk
/// boundaries respect structure wherever possible rather than always cutting at a fixed offset
/// (research.md Decision 4).
/// </summary>
public sealed class RecursiveChunkingStrategy : IChunkingStrategy
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", " "];

    public ChunkingStrategy Strategy => ChunkingStrategy.Recursive;

    public Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var pieces = Split(extractedText, 0);
        var grouped = ChunkTextHelpers.GroupUnitsIntoChunks(pieces, ChunkTextHelpers.DefaultTargetCharacterSize);

        var candidates = grouped
            .Select((content, index) => ChunkTextHelpers.BuildCandidate(content, language, pageNumber: null, section: null, heading: null, position: index))
            .ToList();

        return Task.FromResult<IReadOnlyList<ChunkCandidate>>(candidates);
    }

    private static List<string> Split(string text, int separatorIndex)
    {
        if (text.Length <= ChunkTextHelpers.DefaultTargetCharacterSize || separatorIndex >= Separators.Length)
        {
            return [text.Trim()];
        }

        var separator = Separators[separatorIndex];
        var parts = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);

        var result = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length > ChunkTextHelpers.DefaultTargetCharacterSize)
            {
                result.AddRange(Split(part, separatorIndex + 1));
            }
            else if (!string.IsNullOrWhiteSpace(part))
            {
                result.Add(part.Trim());
            }
        }

        return result.Count > 0 ? result : [text.Trim()];
    }
}
