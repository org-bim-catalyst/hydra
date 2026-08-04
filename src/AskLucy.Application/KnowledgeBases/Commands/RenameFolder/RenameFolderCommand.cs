using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.RenameFolder;

public sealed record RenameFolderCommand(Guid KnowledgeBaseId, Guid FolderId, string Name) : IRequest<KnowledgeBaseFolderDto>;
