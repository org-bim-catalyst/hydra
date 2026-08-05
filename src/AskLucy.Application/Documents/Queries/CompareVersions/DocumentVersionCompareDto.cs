namespace AskLucy.Application.Documents.Queries.CompareVersions;

public sealed record MetadataFieldDiff(string? From, string? To);

/// <summary>
/// contracts/document-versions-folders-api.md's version-compare shape (FR-042). <see cref="MetadataDiff"/>
/// compares each version's own intrinsic fields (<c>originalFileName</c>/<c>sizeBytes</c>/
/// <c>pageCount</c>) rather than <c>DocumentMetadata</c>'s title/author/keywords — those live in
/// a single current-state row per document (data-model.md, unique per <c>DocumentId</c>, not
/// versioned), so there is no per-version metadata snapshot to diff against; only
/// <see cref="Domain.Documents.DocumentVersion"/>'s own stored fields actually vary between two
/// versions. Only fields that actually differ are included.
/// </summary>
public sealed record DocumentVersionCompareDto(string ExtractedTextDiff, IReadOnlyDictionary<string, MetadataFieldDiff> MetadataDiff);
