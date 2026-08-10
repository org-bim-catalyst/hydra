using MediatR;

namespace AskLucy.Application.Prompts.Commands.MoveFolder;

public sealed record MoveFolderCommand(Guid FolderId, Guid? NewParentFolderId) : IRequest<PromptFolderDto>;
