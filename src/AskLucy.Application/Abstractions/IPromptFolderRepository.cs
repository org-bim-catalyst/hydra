using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="PromptFolder"/> — mirrors <c>IKnowledgeBaseFolderRepository</c>'s tree-traversal/cycle-check shape (research.md Decision 5).</summary>
public interface IPromptFolderRepository
{
    Task<PromptFolder?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    void Add(PromptFolder folder);

    /// <summary>The full folder tree for an owner (flat list — the caller builds the tree from `ParentFolderId`), for `GetFolderTreeQuery`.</summary>
    Task<IReadOnlyList<PromptFolder>> GetTreeForOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>True if <paramref name="folderId"/> is <paramref name="potentialAncestorId"/> itself or a descendant of it — used to reject a move that would nest a folder into its own subtree (spec.md Edge Cases).</summary>
    Task<bool> IsSameOrDescendantAsync(Guid folderId, Guid potentialAncestorId, CancellationToken cancellationToken = default);
}
