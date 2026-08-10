using MediatR;

namespace AskLucy.Application.Prompts.Commands.CreateFolder;

public sealed record CreateFolderCommand(string Name, Guid? ParentFolderId) : IRequest<PromptFolderDto>;
