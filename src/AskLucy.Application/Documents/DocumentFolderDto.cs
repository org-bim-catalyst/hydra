using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents;

/// <summary>contracts/document-versions-folders-api.md's folder shape (FR-033). <see cref="DocumentCount"/> is non-deleted documents directly in this folder (not recursive).</summary>
public sealed record DocumentFolderDto(Guid Id, string Name, Guid? ParentFolderId, int Depth, int DocumentCount)
{
    public static DocumentFolderDto FromEntity(DocumentFolder folder, int documentCount) => new(
        folder.Id, folder.Name, folder.ParentFolderId, folder.Depth, documentCount);
}
