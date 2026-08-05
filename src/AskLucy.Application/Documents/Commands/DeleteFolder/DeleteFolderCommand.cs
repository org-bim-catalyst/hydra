using MediatR;

namespace AskLucy.Application.Documents.Commands.DeleteFolder;

/// <summary>contracts/document-versions-folders-api.md's <c>onContainedDocuments</c> choice — required whenever the folder is non-empty (FR-033, Edge Cases).</summary>
public enum OnContainedDocumentsAction
{
    MoveToParent,
    ArchiveAll,
    DeleteAll,
}

/// <summary>contracts/document-versions-folders-api.md `DELETE /api/v1/documents/folders/{id}?onContainedDocuments=...`.</summary>
public sealed record DeleteFolderCommand(Guid FolderId, OnContainedDocumentsAction? OnContainedDocuments) : IRequest;
