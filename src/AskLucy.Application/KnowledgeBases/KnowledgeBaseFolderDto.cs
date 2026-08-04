using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases;

public sealed record KnowledgeBaseFolderDto(Guid Id, Guid KnowledgeBaseId, Guid? ParentFolderId, string Name, int Depth)
{
    public static KnowledgeBaseFolderDto FromEntity(KnowledgeBaseFolder folder) =>
        new(folder.Id, folder.KnowledgeBaseId, folder.ParentFolderId, folder.Name, folder.Depth);
}
