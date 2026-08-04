using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="KnowledgeBaseDocument"/> — has independent query needs from <see cref="KnowledgeBase"/> itself (mirrors why <c>Message</c> has its own repository separate from <c>UserChat</c>). Extended in specs/014-knowledge-base-management US2 (T048) with upload/move/delete-supporting methods; only the read used by Purge's cascade-file-deletion (FR-036) is needed by US1.</summary>
public interface IKnowledgeBaseDocumentRepository
{
    Task<KnowledgeBaseDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(KnowledgeBaseDocument document);

    /// <summary>Every document for a knowledge base, including already-soft-deleted ones — the permanent-purge cascade (FR-036) must delete every associated file regardless of the document's own soft-delete state.</summary>
    Task<IReadOnlyList<KnowledgeBaseDocument>> ListByKnowledgeBaseIdIncludingDeletedAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>Documents directly inside one folder (or, when <paramref name="folderId"/> is null, at the knowledge base's root) — the tree view loads a folder's contents lazily, one level at a time, not the whole knowledge base at once.</summary>
    Task<IReadOnlyList<KnowledgeBaseDocument>> ListByFolderAsync(Guid knowledgeBaseId, Guid? folderId, CancellationToken cancellationToken = default);
}
