using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary><see cref="StructureJson"/> holds headings/paragraphs/tables/lists/captions/footnotes/hyperlinks/page-number structure (FR-022), serialized as JSON — never queried by sub-field, so no normalized shape is needed.</summary>
/// <summary><see cref="Title"/>/<see cref="Author"/>/<see cref="CreationDateUtc"/>/<see cref="ModificationDateUtc"/>/<see cref="Keywords"/> are the document's own embedded core properties (FR-023), read alongside the text/structure in the same pass rather than a second file-open.</summary>
public sealed record DocumentTextExtractionResult(
    string? PlainText,
    string? StructureJson,
    int? PageCount,
    string? Title = null,
    string? Author = null,
    DateTime? CreationDateUtc = null,
    DateTime? ModificationDateUtc = null,
    string? Keywords = null);

/// <summary>
/// Structured text/metadata extraction (FR-022, research.md Decision 5). One implementation per
/// format family — <c>OpenXmlTextExtractor</c> (DOCX/XLSX/PPTX), <c>DocnetPdfTextExtractor</c>
/// (PDF) — resolved by the pipeline via <see cref="CanHandle"/> (Strategy pattern), never by
/// concrete type.
/// </summary>
public interface IDocumentTextExtractor
{
    bool CanHandle(DocumentFileType fileType);

    Task<DocumentTextExtractionResult> ExtractAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default);
}
