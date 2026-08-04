using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="KnowledgeBaseFolder"/> — has independent query needs (tree traversal, descendant checks, non-empty checks) from <see cref="KnowledgeBase"/> itself, mirroring why <c>Message</c> has its own repository separate from <c>UserChat</c>.</summary>
public interface IKnowledgeBaseFolderRepository
{
    Task<KnowledgeBaseFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(KnowledgeBaseFolder folder);

    /// <summary>The full folder tree for a knowledge base (flat list — the caller builds the tree from `ParentFolderId`), for `GetKnowledgeBaseFolderTreeQuery`.</summary>
    Task<IReadOnlyList<KnowledgeBaseFolder>> ListByKnowledgeBaseIdAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>True if <paramref name="folderId"/> is <paramref name="potentialAncestorId"/> itself or a descendant of it — used to reject a move that would nest a folder into its own subtree (FR-013).</summary>
    Task<bool> IsSameOrDescendantAsync(Guid folderId, Guid potentialAncestorId, CancellationToken cancellationToken = default);

    /// <summary>Whether the folder currently contains any subfolders or (non-deleted) documents — drives the confirm-if-non-empty delete rule (FR-015).</summary>
    Task<bool> HasContentsAsync(Guid folderId, CancellationToken cancellationToken = default);
}
