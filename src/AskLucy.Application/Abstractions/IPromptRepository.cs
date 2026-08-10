using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

public enum PromptListView
{
    All,
    Favorites,
    Pinned,
    RecentlyUsed,
    RecentlyModified,
    Archived,
}

/// <summary>Aggregate-oriented repository for <see cref="Prompt"/> (constitution §3 Repository rules).</summary>
public interface IPromptRepository
{
    Task<Prompt?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup used by the name-uniqueness pre-check (research.md Decision 7, FR-006).</summary>
    Task<Prompt?> GetByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default);

    void Add(Prompt prompt);

    /// <summary>Every prompt directly inside a folder — used by `DeleteFolderCommand` to unfile them (FolderId set to null) before the folder itself is removed.</summary>
    Task<IReadOnlyList<Prompt>> ListByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default);

    Task<PromptVersion?> GetVersionAsync(Guid promptId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>Looked up by the version's own id — used where a caller (e.g. <c>PromptExecution.PromptVersionId</c>) only has that, not the version number.</summary>
    Task<PromptVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromptVersion>> ListVersionsAsync(Guid promptId, CancellationToken cancellationToken = default);

    /// <summary>Cursor-based (keyset) search/filter/sort over the caller's own prompts (FR-050–FR-053) — full-text `query` against the `FULLTEXT INDEX` (research.md Decision 12), category/tag/folder/status filters, and the recently-used/recently-modified/favorites/pinned views.</summary>
    Task<(IReadOnlyList<Prompt> Items, string? NextCursor)> SearchAsync(
        string ownerId,
        PromptListView view,
        string? query,
        Guid? categoryId,
        string? tag,
        Guid? folderId,
        PromptStatus? status,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Distinct tag values the caller has used across their own prompts (FR-052, `ListTagsQuery`) — queries the `PromptTags` table directly, mirroring `KnowledgeBaseTagConfiguration`'s documented reasoning.</summary>
    Task<IReadOnlyList<string>> ListDistinctTagValuesAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>1:1 with <see cref="Prompt"/> — not a separate top-level repository (data-model.md); accessed here since every caller already has a <see cref="Prompt"/> id in hand.</summary>
    Task<PromptUsageStatistics?> GetUsageStatisticsAsync(Guid promptId, CancellationToken cancellationToken = default);

    /// <summary>Batch lookup for list/search views — avoids an N+1 loop over <see cref="GetUsageStatisticsAsync"/> per row (constitution &#167;5).</summary>
    Task<IReadOnlyDictionary<Guid, PromptUsageStatistics>> GetUsageStatisticsByPromptIdsAsync(
        IReadOnlyCollection<Guid> promptIds, CancellationToken cancellationToken = default);

    void AddUsageStatistics(PromptUsageStatistics statistics);
}
