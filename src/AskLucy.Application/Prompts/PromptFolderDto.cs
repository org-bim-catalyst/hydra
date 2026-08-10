using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Prompts;

public sealed record PromptFolderDto(Guid Id, Guid? ParentFolderId, string Name, int Depth)
{
    public static PromptFolderDto FromEntity(PromptFolder folder) => new(folder.Id, folder.ParentFolderId, folder.Name, folder.Depth);
}
