using AskLucy.Domain.Memory;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="MemoryEntity"/> (constitution §3 Repository rules).</summary>
public interface IMemoryRepository
{
    Task<MemoryEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Hydrates candidate ids into entities, excluding any no longer <see cref="MemoryLifecycleState.Active"/> or with an open (<see cref="MemoryConflictResolutionStatus.PendingUserConfirmation"/>) conflict (research.md Decisions 4/10) — used by <c>MemoryService</c> after <see cref="IMemoryVectorStore"/> narrows the candidate set.</summary>
    Task<IReadOnlyList<MemoryEntity>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>The candidate pool for conflict detection (research.md Decision 10) — active memories for this user (general + the given project, if any), hydrated by id after vector-similarity narrowing.</summary>
    Task<IReadOnlyList<MemoryEntity>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// spec.md FR-017/FR-018 — cursor-paginated list/search, filtered client-side by
    /// <paramref name="query"/> against decrypted content (research.md Decision 12 — content is
    /// encrypted at rest, so free-text search cannot be pushed down as SQL <c>LIKE</c>) after
    /// narrowing by the indexed columns. <paramref name="projectId"/> null + <paramref name="generalOnly"/>
    /// false means "every project scope, unfiltered" (contracts/memories-api.md — the
    /// <c>projectId</c> query parameter simply omitted); <paramref name="generalOnly"/> true means
    /// "only memories with no project" (the literal <c>projectId=general</c>), regardless of
    /// <paramref name="projectId"/>.
    /// </summary>
    Task<IReadOnlyList<MemoryEntity>> SearchAsync(
        string userId, MemoryCategory? category, MemoryLifecycleState? state, Guid? projectId, bool generalOnly,
        string? query, Guid? afterId, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>The total count matching the same filters as <see cref="SearchAsync"/>, ignoring cursor/page size (contracts/memories-api.md's <c>totalCount</c>).</summary>
    Task<int> CountAsync(
        string userId, MemoryCategory? category, MemoryLifecycleState? state, Guid? projectId, bool generalOnly,
        string? query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntity>> GetAllByUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryEntity>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Reinforcement candidates (spec.md Edge Case — "same fact stated many times") — active memories in the same category whose content might match a newly detected statement, for the extraction job to check before creating a duplicate.</summary>
    Task<IReadOnlyList<MemoryEntity>> GetActiveByCategoryAsync(string userId, Guid? projectId, MemoryCategory category, CancellationToken cancellationToken = default);

    /// <summary>Background cleanup candidates (spec.md FR-031, research.md Decision 18).</summary>
    Task<IReadOnlyList<MemoryEntity>> GetCleanupCandidatesAsync(DateTime nowUtc, DateTime staleArchivedBeforeUtc, int batchSize, CancellationToken cancellationToken = default);

    void Add(MemoryEntity memory);
}
