using System.Text.Json.Serialization;

namespace AskLucy.Infrastructure.Documents.Extraction;

/// <summary>The JSON shape persisted in <c>DocumentVersion.ExtractedStructureJson</c> (FR-022) — a flat, ordered list rather than a nested tree, since nothing in this spec queries it by sub-field (data-model.md).</summary>
public sealed record DocumentStructureElement(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("level")] int? Level = null,
    [property: JsonPropertyName("pageNumber")] int? PageNumber = null,
    [property: JsonPropertyName("url")] string? Url = null);
