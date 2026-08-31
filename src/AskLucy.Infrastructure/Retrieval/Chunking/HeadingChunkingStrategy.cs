using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using AskLucy.Infrastructure.Documents.Extraction;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>
/// FR-001 — uses the Document Intelligence Pipeline's already-extracted structure (specs/015
/// FR-022, <see cref="DocumentStructureElement"/>) to split at "heading"-type elements, one chunk
/// per section, carrying the heading text and page number for citations (FR-002). Falls back to
/// <see cref="ParagraphChunkingStrategy"/>'s behavior when no structure JSON is available.
/// </summary>
public sealed partial class HeadingChunkingStrategy(ParagraphChunkingStrategy fallback, ILogger<HeadingChunkingStrategy> logger) : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Heading;

    public async Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var elements = TryDeserialize(extractedStructureJson);
        if (elements is null || elements.Count == 0)
        {
            return await fallback.ChunkAsync(extractedText, extractedStructureJson, language, cancellationToken);
        }

        var candidates = new List<ChunkCandidate>();
        var position = 0;
        string? currentHeading = null;
        int? currentPageNumber = null;
        var currentContent = new System.Text.StringBuilder();

        void Flush()
        {
            var content = currentContent.ToString().Trim();
            if (content.Length > 0)
            {
                candidates.Add(ChunkTextHelpers.BuildCandidate(content, language, currentPageNumber, currentHeading, currentHeading, position++));
            }

            currentContent.Clear();
        }

        foreach (var element in elements)
        {
            if (element.Type.Equals("heading", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                currentHeading = element.Text.Trim();
                currentPageNumber = element.PageNumber;
            }
            else
            {
                currentPageNumber ??= element.PageNumber;
                currentContent.AppendLine(element.Text);
            }
        }

        Flush();

        return candidates.Count > 0
            ? candidates
            : await fallback.ChunkAsync(extractedText, extractedStructureJson, language, cancellationToken);
    }

    private List<DocumentStructureElement>? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<DocumentStructureElement>>(json);
        }
        catch (JsonException ex)
        {
            // Not a silent failure (constitution §2.VIII): logged, then this chunking strategy
            // degrades to ParagraphChunkingStrategy rather than throwing — a malformed structure
            // JSON for one document must not block indexing every other document in the batch.
            LogStructureDeserializationFailed(ex);
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to deserialize ExtractedStructureJson for heading-based chunking; falling back to paragraph chunking.")]
    private partial void LogStructureDeserializationFailed(Exception exception);
}
