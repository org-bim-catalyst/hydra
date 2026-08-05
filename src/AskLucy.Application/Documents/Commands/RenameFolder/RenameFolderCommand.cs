using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RenameFolder;

/// <summary>contracts/document-versions-folders-api.md `PATCH /api/v1/documents/folders/{id}`.</summary>
public sealed record RenameFolderCommand(Guid FolderId, string Name) : IRequest<DocumentFolderDto>;
