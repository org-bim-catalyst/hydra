using AskLucy.Application.KnowledgeBases;
using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="KnowledgeBase"/> (constitution §3 Repository rules).</summary>
public interface IKnowledgeBaseRepository
{
    Task<KnowledgeBase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Looks up a knowledge base by id bypassing the global soft-delete query filter — needed by Restore/Purge, which must act on an already-soft-deleted knowledge base.</summary>
    Task<KnowledgeBase?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(KnowledgeBase knowledgeBase);

    /// <summary>
    /// Permanently, irreversibly removes a knowledge base — cascades (via database FK
    /// constraints) to its folders/documents/tags. The audit log entry and every document's
    /// underlying file MUST already have been handled by the caller before this runs
    /// (FR-036 — logged, then files deleted, then the row itself). Implemented as a bulk
    /// <c>ExecuteDelete</c> rather than load+Remove+SaveChanges: the Persistence layer's audit
    /// SaveChanges interceptor unconditionally converts every tracked hard delete on a
    /// <c>BaseEntity</c> into a soft delete (mirrors <c>IUserChatRepository.PurgeAsync</c>).
    /// </summary>
    Task PurgeAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every soft-deleted knowledge base whose <see cref="KnowledgeBase.PurgeScheduledAtUtc"/> has elapsed (FR-036) — read by the periodic purge sweep, across all owners.</summary>
    Task<IReadOnlyList<KnowledgeBase>> ListPastPurgeScheduleAsync(DateTime asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>Distinct tag values the caller has used across their own knowledge bases (FR-020, `ListTagsQuery`), optionally prefix-filtered.</summary>
    Task<IReadOnlyList<string>> ListDistinctTagValuesAsync(string ownerId, string? prefix, CancellationToken cancellationToken = default);

    /// <summary>Every knowledge base referencing a category — used by `DeleteCategoryCommand` to clear the FK on all of them before the category itself is removed (FR-021).</summary>
    Task<IReadOnlyList<KnowledgeBase>> ListByCategoryIdAsync(Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>Aggregate counts for the dashboard summary cards (FR-029) — one query, not the naive N+1 of calling <see cref="SearchAsync"/> five times with different filters.</summary>
    Task<KnowledgeBaseDashboardSummaryDto> GetDashboardSummaryAsync(string ownerId, DateTime recentSinceUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cursor-based (keyset) search/filter/sort over the caller's own knowledge bases
    /// (FR-022–FR-024). Pinned knowledge bases always sort ahead of unpinned ones (FR-028) —
    /// modeled as an ascending "pin rank" (0 = pinned) that is always the first ORDER BY/
    /// cursor key, regardless of <paramref name="sort"/> — mirrors
    /// `IUserChatRepository.SearchAsync`.
    /// </summary>
    Task<(IReadOnlyList<KnowledgeBase> Items, string? NextCursor)> SearchAsync(
        string ownerId,
        KnowledgeBaseListView view,
        string? query,
        Guid? categoryId,
        string? tag,
        bool? favoriteOnly,
        bool? pinnedOnly,
        KnowledgeBaseSort sort,
        bool sortDescending,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}
