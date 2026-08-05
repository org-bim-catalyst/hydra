using AskLucy.Application.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CreateFolder;

/// <summary>contracts/document-versions-folders-api.md `POST /api/v1/documents/folders` (FR-033).</summary>
public sealed record CreateFolderCommand(string Name, Guid? ParentFolderId) : IRequest<DocumentFolderDto>;
