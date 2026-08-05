using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>FR-001 — splits text into fixed-size character windows with no structural awareness (research.md Decision 4).</summary>
public sealed class FixedSizeChunkingStrategy : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.FixedSize;

    public Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var candidates = new List<ChunkCandidate>();
        var position = 0;

        for (var offset = 0; offset < extractedText.Length; offset += ChunkTextHelpers.DefaultTargetCharacterSize)
        {
            var length = Math.Min(ChunkTextHelpers.DefaultTargetCharacterSize, extractedText.Length - offset);
            var content = extractedText.Substring(offset, length).Trim();

            if (content.Length == 0)
            {
                continue;
            }

            candidates.Add(ChunkTextHelpers.BuildCandidate(content, language, pageNumber: null, section: null, heading: null, position: position++));
        }

        return Task.FromResult<IReadOnlyList<ChunkCandidate>>(candidates);
    }
}
