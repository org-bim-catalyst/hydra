using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using AskLucy.Infrastructure.Documents.Extraction;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Retrieval.Chunking;

/// <summary>
/// FR-001 — keeps each "table"-type structure element (specs/015 FR-022) as its own, intact
/// chunk (never sub-split, so a table's rows/columns aren't separated across chunks), with the
/// surrounding prose grouped via <see cref="ParagraphChunkingStrategy"/>'s behavior. Falls back
/// entirely to paragraph chunking when no structure JSON is available.
/// </summary>
public sealed class TableChunkingStrategy(ParagraphChunkingStrategy fallback, ILogger<TableChunkingStrategy> logger) : IChunkingStrategy
{
    public ChunkingStrategy Strategy => ChunkingStrategy.Table;

    public async Task<IReadOnlyList<ChunkCandidate>> ChunkAsync(string extractedText, string? extractedStructureJson, string? language, CancellationToken cancellationToken = default)
    {
        var elements = TryDeserialize(extractedStructureJson);
        if (elements is null || elements.Count == 0)
        {
            return await fallback.ChunkAsync(extractedText, extractedStructureJson, language, cancellationToken);
        }

        var candidates = new List<ChunkCandidate>();
        var position = 0;
        var proseUnits = new List<(string Text, int? PageNumber)>();

        void FlushProse()
        {
            if (proseUnits.Count == 0)
            {
                return;
            }

            var grouped = ChunkTextHelpers.GroupUnitsIntoChunks(proseUnits.Select(p => p.Text).ToList(), ChunkTextHelpers.DefaultTargetCharacterSize);
            var pageNumber = proseUnits[0].PageNumber;
            foreach (var content in grouped)
            {
                candidates.Add(ChunkTextHelpers.BuildCandidate(content, language, pageNumber, section: null, heading: null, position: position++));
            }

            proseUnits.Clear();
        }

        foreach (var element in elements)
        {
            if (element.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
            {
                FlushProse();
                if (!string.IsNullOrWhiteSpace(element.Text))
                {
                    candidates.Add(ChunkTextHelpers.BuildCandidate(element.Text.Trim(), language, element.PageNumber, section: "Table", heading: null, position: position++));
                }
            }
            else if (!string.IsNullOrWhiteSpace(element.Text))
            {
                proseUnits.Add((element.Text, element.PageNumber));
            }
        }

        FlushProse();

        return candidates.Count > 0
            ? candidates
            : await fallback.ChunkAsync(extractedText, extractedStructureJson, language, cancellationToken);
    }

    private IReadOnlyList<DocumentStructureElement>? TryDeserialize(string? json)
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
            logger.LogWarning(ex, "Failed to deserialize ExtractedStructureJson for table-aware chunking; falling back to paragraph chunking.");
            return null;
        }
    }
}
