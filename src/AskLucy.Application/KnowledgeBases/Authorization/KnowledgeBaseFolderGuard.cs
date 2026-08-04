using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases.Authorization;

/// <summary>Centralizes the "does this folder belong to this knowledge base" check shared by every folder/document handler — a folder has no `OwnerId` of its own, so this is checked after <see cref="KnowledgeBaseOwnershipGuard"/> has already confirmed the caller owns the parent knowledge base.</summary>
public static class KnowledgeBaseFolderGuard
{
    public static KnowledgeBaseFolder EnsureBelongsTo(KnowledgeBaseFolder? folder, Guid knowledgeBaseId)
    {
        if (folder is null || folder.KnowledgeBaseId != knowledgeBaseId)
        {
            throw new KeyNotFoundException("Folder not found.");
        }

        return folder;
    }
}
