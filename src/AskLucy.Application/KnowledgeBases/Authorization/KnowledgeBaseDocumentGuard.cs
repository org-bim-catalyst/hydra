using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases.Authorization;

/// <summary>Centralizes the "does this document belong to this knowledge base" check, mirroring <see cref="KnowledgeBaseFolderGuard"/>.</summary>
public static class KnowledgeBaseDocumentGuard
{
    public static KnowledgeBaseDocument EnsureBelongsTo(KnowledgeBaseDocument? document, Guid knowledgeBaseId)
    {
        if (document is null || document.KnowledgeBaseId != knowledgeBaseId)
        {
            throw new KeyNotFoundException("Document not found.");
        }

        return document;
    }
}
