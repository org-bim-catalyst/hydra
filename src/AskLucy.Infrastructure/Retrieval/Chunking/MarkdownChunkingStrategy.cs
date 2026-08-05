using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>FR-001 — splits on Markdown heading syntax (<c>#</c>…<c>######</c>) directly in the extracted text, one chunk per section (research.md Decision 4). Falls back to <see cref="ParagraphChunkingStrategy"/>'s grouping when a section still exceeds the target size.</summary>
public sealed partial class MarkdownChunkingStrategy : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Markdown;

    public Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var lines = extractedText.Replace("\r\n", "\n").Split('\n');
        var sections = new List<(string? Heading, string Content)>();
        var currentHeading = (string?)null;
        var currentContent = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var headingMatch = MarkdownHeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                if (currentContent.Length > 0)
                {
                    sections.Add((currentHeading, currentContent.ToString().Trim()));
                    currentContent.Clear();
                }

                currentHeading = headingMatch.Groups["text"].Value.Trim();
                currentContent.AppendLine(line);
            }
            else
            {
                currentContent.AppendLine(line);
            }
        }

        if (currentContent.Length > 0)
        {
            sections.Add((currentHeading, currentContent.ToString().Trim()));
        }

        var candidates = new List<ChunkCandidate>();
        var position = 0;

        foreach (var (heading, content) in sections)
        {
            if (content.Length == 0)
            {
                continue;
            }

            if (content.Length <= ChunkTextHelpers.DefaultTargetCharacterSize)
            {
                candidates.Add(ChunkTextHelpers.BuildCandidate(content, language, pageNumber: null, section: heading, heading: heading, position: position++));
                continue;
            }

            var paragraphs = content.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var group in ChunkTextHelpers.GroupUnitsIntoChunks(paragraphs, ChunkTextHelpers.DefaultTargetCharacterSize))
            {
                candidates.Add(ChunkTextHelpers.BuildCandidate(group, language, pageNumber: null, section: heading, heading: heading, position: position++));
            }
        }

        return Task.FromResult<IReadOnlyList<ChunkCandidate>>(candidates);
    }

    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<text>.+)$")]
    private static partial Regex MarkdownHeadingRegex();
}
