using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Infrastructure.Documents.Extraction;

/// <summary>
/// Structured extraction for the plain-text-shaped formats (FR-022, research.md Decision 5):
/// Markdown, CSV, and plain text. Unlike <see cref="OpenXmlTextExtractor"/>/<see
/// cref="DocnetPdfTextExtractor"/>, there is no embedded structure to parse — the whole file's
/// text content is already the plain text, and <see cref="DocumentTextExtractionResult.StructureJson"/>
/// stays <see langword="null"/>.
///
/// <para>
/// Pre-existing gap fixed here rather than in <c>IndexingOrchestrator</c> or
/// <c>DocumentProcessingPipeline</c> (specs/067-notifications-communication-hub T236): no
/// <see cref="IDocumentTextExtractor"/> handled <see cref="DocumentFileType.Markdown"/>/<see
/// cref="DocumentFileType.Csv"/>/<see cref="DocumentFileType.Text"/> before this class existed,
/// so <c>IndexingOrchestrator.CreateDocumentFromKnowledgeBaseDocumentAsync</c> threw
/// <see cref="InvalidOperationException"/> for every knowledge-base upload of these types —
/// including the Markdown upload T236's own Independent Test exercises. Scoped narrowly (this
/// file only) rather than touched as part of T237/T238, which document
/// <c>IndexingOrchestrator</c> as deliberately unchanged by this feature.
/// </para>
/// </summary>
public sealed class PlainTextExtractor : IDocumentTextExtractor
{
    public bool CanHandle(DocumentFileType fileType) =>
        fileType is DocumentFileType.Markdown or DocumentFileType.Csv or DocumentFileType.Text;

    public async Task<DocumentTextExtractionResult> ExtractAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);

        return new DocumentTextExtractionResult(text, StructureJson: null, PageCount: 1);
    }
}
