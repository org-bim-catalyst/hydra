using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>
/// Shared helpers for the non-semantic <see cref="IChunkingStrategy"/> implementations
/// (research.md Decision 4). No tokenizer dependency is introduced (constitution §2.III) — token
/// count is estimated via the widely-used ~4-characters-per-token heuristic, adequate for FR-003's
/// "record token count" and FR-024's context-budget trimming, neither of which requires exact
/// provider-specific tokenization.
/// </summary>
internal static class ChunkTextHelpers
{
    public const int DefaultTargetCharacterSize = 1500;

    public static int EstimateTokenCount(string text) => Math.Max(1, text.Length / 4);

    public static ChunkCandidate BuildCandidate(string content, string? language, int? pageNumber, string? section, string? heading, int position) =>
        new(
            Content: content,
            TokenCount: EstimateTokenCount(content),
            CharacterCount: content.Length,
            Language: language,
            PageNumber: pageNumber,
            Section: section,
            Heading: heading,
            Position: position);

    /// <summary>Groups a sequence of atomic text units (paragraphs, sentences, lines) into chunks that stay within <paramref name="targetCharacterSize"/> where possible, never splitting a single unit that already exceeds it.</summary>
    public static IReadOnlyList<string> GroupUnitsIntoChunks(IReadOnlyList<string> units, int targetCharacterSize)
    {
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var unit in units)
        {
            if (current.Length > 0 && current.Length + unit.Length + 2 > targetCharacterSize)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(unit);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks;
    }
}
