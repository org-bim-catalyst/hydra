namespace AskLucy.Application.Documents.Queries.GetDocumentPreview;

/// <summary>contracts/documents-api.md's preview shape (FR-043, FR-044) — a superset of <see cref="Domain.Documents.DocumentPreviewType"/> with an extra `Unavailable` case for "no preview exists," which is never an error state.</summary>
public enum DocumentPreviewKind
{
    PageImage,
    Thumbnail,
    StructuredContent,
    Unavailable,
}

/// <summary><see cref="PreviewId"/> is set only for <see cref="DocumentPreviewKind.PageImage"/>/<see cref="DocumentPreviewKind.Thumbnail"/> — the controller signs it into a download URL (mirrors the document-download pattern). <see cref="StructuredContent"/> is set only for <see cref="DocumentPreviewKind.StructuredContent"/>.</summary>
public sealed record DocumentPreviewResultDto(DocumentPreviewKind PreviewType, Guid? PreviewId, string? StructuredContent)
{
    public static readonly DocumentPreviewResultDto Unavailable = new(DocumentPreviewKind.Unavailable, null, null);
}
