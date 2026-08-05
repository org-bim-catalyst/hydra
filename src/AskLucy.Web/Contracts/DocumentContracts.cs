using AskLucy.Application.Documents.Queries.GetDocumentPreview;

namespace AskLucy.Web.Contracts;

/// <summary>contracts/documents-api.md `POST /api/v1/documents/uploads`.</summary>
public sealed record StartUploadRequest(string FileName, long SizeBytes);

/// <summary>contracts/documents-api.md `POST .../complete-as-version`.</summary>
public sealed record CompleteUploadAsVersionRequest(Guid ExistingDocumentId, string VersionIncrement);

public sealed record RenameDocumentRequest(string FileName);

/// <summary>contracts/documents-api.md `GET .../download` — a signed, time-limited URL the client navigates to directly (already <c>[AllowAnonymous]</c>-authorized by its signature), not a redirect (see <c>DocumentsController.Download</c>'s doc comment for why).</summary>
public sealed record DocumentDownloadUrlResponse(string Url, string FileName);

/// <summary>contracts/documents-api.md `PATCH .../metadata` (FR-031). Only supplied fields change.</summary>
public sealed record UpdateDocumentMetadataRequest(
    byte[] RowVersion, string? Title, string? Author, DateTime? CreationDate, DateTime? ModificationDate, string? Keywords);

/// <summary>contracts/documents-api.md `PUT .../classification` (FR-026).</summary>
public sealed record OverrideClassificationRequest(Guid CategoryId);

/// <summary>contracts/documents-api.md `POST .../tags` (FR-032).</summary>
public sealed record AddTagRequest(string Name);

/// <summary>contracts/documents-api.md `PATCH .../folder` (FR-033). Null moves to the root level. Distinct name from <c>KnowledgeBaseContracts.MoveDocumentRequest</c> — same shape, different bounded context.</summary>
public sealed record MoveDocumentToFolderRequest(Guid? FolderId);

/// <summary>contracts/document-versions-folders-api.md `POST /api/v1/documents/folders` (FR-033). Distinct name from <c>KnowledgeBaseContracts.CreateFolderRequest</c> — same shape, different bounded context (research.md Decision 1).</summary>
public sealed record CreateDocumentFolderRequest(string Name, Guid? ParentFolderId);

public sealed record RenameDocumentFolderRequest(string Name);

/// <summary>contracts/document-versions-folders-api.md `PATCH .../folders/{id}/parent`.</summary>
public sealed record MoveDocumentFolderRequest(Guid? ParentFolderId);

/// <summary>contracts/document-versions-folders-api.md `POST /api/v1/documents/{documentId}/versions` (FR-038, FR-039).</summary>
public sealed record ReplaceDocumentRequest(Guid UploadSessionId, string VersionIncrement);

/// <summary>contracts/documents-api.md `GET .../preview` (FR-043, FR-044). <c>Url</c> is a signed, time-limited URL (mirrors <see cref="DocumentDownloadUrlResponse"/>), set only for PageImage/Thumbnail.</summary>
public sealed record DocumentPreviewResponse(DocumentPreviewKind PreviewType, string? Url, string? StructuredContent);
