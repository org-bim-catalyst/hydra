using MediatR;

namespace AskLucy.Application.Prompts.Commands.RenameFolder;

public sealed record RenameFolderCommand(Guid FolderId, string Name) : IRequest<PromptFolderDto>;
