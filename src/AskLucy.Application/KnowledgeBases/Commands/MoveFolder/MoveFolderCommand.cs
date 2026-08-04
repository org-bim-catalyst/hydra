using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.MoveFolder;

/// <summary><paramref name="NewParentFolderId"/> null moves the folder to the knowledge base's root.</summary>
public sealed record MoveFolderCommand(Guid KnowledgeBaseId, Guid FolderId, Guid? NewParentFolderId) : IRequest<KnowledgeBaseFolderDto>;
