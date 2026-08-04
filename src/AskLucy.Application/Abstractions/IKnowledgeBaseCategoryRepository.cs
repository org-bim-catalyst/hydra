using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="KnowledgeBaseCategory"/> (constitution §3 Repository rules).</summary>
public interface IKnowledgeBaseCategoryRepository
{
    Task<KnowledgeBaseCategory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(KnowledgeBaseCategory category);

    /// <summary>Marks the category for removal — the audit SaveChanges interceptor converts this into a soft delete, mirroring every other <c>BaseEntity</c>.</summary>
    void Remove(KnowledgeBaseCategory category);

    /// <summary>The 8 predefined (shared, <c>OwnerId == null</c>) categories plus the caller's own private custom ones — never another user's (FR-038).</summary>
    Task<IReadOnlyList<KnowledgeBaseCategory>> ListPredefinedAndOwnedAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive duplicate check scoped to one owner's custom categories (data-model.md validation rule).</summary>
    Task<bool> ExistsByNameForOwnerAsync(string ownerId, string name, CancellationToken cancellationToken = default);
}
