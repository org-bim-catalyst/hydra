using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.MoveFolder;

/// <summary>contracts/document-versions-folders-api.md `PATCH /api/v1/documents/folders/{id}/parent` — rejects moving into itself or its own descendant (Edge Cases).</summary>
public sealed record MoveFolderCommand(Guid FolderId, Guid? NewParentFolderId) : IRequest<DocumentFolderDto>;
