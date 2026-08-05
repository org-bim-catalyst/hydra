using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary><paramref name="Content"/> is the rendered image/page bytes; null for a <see cref="DocumentPreviewType.StructuredContent"/> result, which reuses the already-extracted structure instead (research.md Decision 6).</summary>
public sealed record DocumentPreviewResult(DocumentPreviewType PreviewType, byte[]? Content, int? PageNumber);

/// <summary>
/// Preview/thumbnail generation (FR-043, research.md Decision 6). PDF and image formats produce
/// rendered previews; Office formats produce a <see cref="DocumentPreviewType.StructuredContent"/>
/// result the caller persists without calling this interface at all (the structure already
/// exists from <see cref="IDocumentTextExtractor"/>) — see the pipeline's Preview Generation
/// stage for that branch.
/// </summary>
public interface IDocumentPreviewGenerator
{
    bool CanHandle(DocumentFileType fileType);

    Task<IReadOnlyList<DocumentPreviewResult>> GenerateAsync(Stream content, DocumentFileType fileType, CancellationToken cancellationToken = default);
}
