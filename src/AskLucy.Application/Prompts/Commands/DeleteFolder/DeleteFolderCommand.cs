using MediatR;

namespace AskLucy.Application.Prompts.Commands.DeleteFolder;

/// <summary>Deletes a folder — prompts directly inside it become unfiled, never deleted (data-model.md `PromptFolder`).</summary>
public sealed record DeleteFolderCommand(Guid FolderId) : IRequest;
