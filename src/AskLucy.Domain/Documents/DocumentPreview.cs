using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

public enum DocumentPreviewType
{
    PageImage,
    Thumbnail,

    /// <summary>The Office-preview approach (research.md Decision 6) — reuses <see cref="DocumentVersion.ExtractedStructureJson"/> instead of a rendered image.</summary>
    StructuredContent,
}

/// <summary>A generated, renderable preview artifact (FR-043, FR-044, data-model.md).</summary>
public sealed class DocumentPreview : BaseEntity
{
    public Guid DocumentVersionId { get; private set; }

    public DocumentPreviewType PreviewType { get; private set; }

    /// <summary>The <c>IFileStorage</c>-minted name for <see cref="DocumentPreviewType.PageImage"/>/<see cref="DocumentPreviewType.Thumbnail"/>; null for <see cref="DocumentPreviewType.StructuredContent"/>.</summary>
    public string? StoredFileName { get; private set; }

    /// <summary>For multi-page <see cref="DocumentPreviewType.PageImage"/> previews.</summary>
    public int? PageNumber { get; private set; }

    private DocumentPreview()
    {
        // Required by EF Core materialization.
    }

    public static DocumentPreview Create(Guid documentVersionId, DocumentPreviewType previewType, string? storedFileName, int? pageNumber, string actor)
    {
        return new DocumentPreview
        {
            Id = Guid.CreateVersion7(),
            DocumentVersionId = documentVersionId,
            PreviewType = previewType,
            StoredFileName = storedFileName,
            PageNumber = pageNumber,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
