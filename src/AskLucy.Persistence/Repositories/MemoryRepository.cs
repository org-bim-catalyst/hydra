using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.EntityFrameworkCore;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Persistence.Repositories;

public sealed class MemoryRepository(AskLucyDbContext dbContext) : IMemoryRepository
{
    public Task<MemoryEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Memories.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MemoryEntity>> GetActiveByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // EF Core can't translate SelectMany over a per-row array literal (new[] { ... }) into
        // SQL — the two id columns are projected server-side instead, then flattened/filtered
        // client-side; open conflicts are few, so this never approaches the ids/Memories scale.
        var openConflicts = await dbContext.Set<MemoryConflict>()
            .Where(c => c.ResolutionStatus == MemoryConflictResolutionStatus.PendingUserConfirmation)
            .Select(c => new { c.ExistingMemoryId, c.NewMemoryId })
            .ToListAsync(cancellationToken);
        var openConflictMemoryIds = openConflicts
            .SelectMany(c => new[] { c.ExistingMemoryId, c.NewMemoryId ?? Guid.Empty })
            .Where(id => id != Guid.Empty)
            .ToList();

        return await dbContext.Memories
            .Where(m => ids.Contains(m.Id) && m.State == MemoryLifecycleState.Active && !openConflictMemoryIds.Contains(m.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryEntity>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await dbContext.Memories.Where(m => ids.Contains(m.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryEntity>> SearchAsync(
        string userId, MemoryCategory? category, MemoryLifecycleState? state, Guid? projectId, bool generalOnly,
        string? query, Guid? afterId, int pageSize, CancellationToken cancellationToken = default)
    {
        var queryable = dbContext.Memories.Where(m => m.UserId == userId);

        if (category is not null)
        {
            queryable = queryable.Where(m => m.Category == category);
        }

        if (state is not null)
        {
            queryable = queryable.Where(m => m.State == state);
        }

        if (generalOnly)
        {
            queryable = queryable.Where(m => m.ProjectId == null);
        }
        else if (projectId is not null)
        {
            queryable = queryable.Where(m => m.ProjectId == projectId);
        }

        // Free-text search (FR-018) cannot be pushed down as SQL — Content is encrypted at rest
        // (research.md Decision 12), so EF's value converter cannot translate `Contains` into SQL
        // `LIKE`. The indexed filters above bound the candidate set to one user's memories (and,
        // where given, one category/state/project) before this client-side pass runs, keeping it
        // well within SC-006's "thousands of memories per user" scale.
        var candidates = await queryable.OrderByDescending(m => m.CreatedAtUtc).ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(query))
        {
            candidates = candidates
                .Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (afterId is not null)
        {
            var afterIndex = candidates.FindIndex(m => m.Id == afterId);
            candidates = afterIndex >= 0 ? candidates.Skip(afterIndex + 1).ToList() : candidates;
        }

        return candidates.Take(pageSize).ToList();
    }

    public async Task<int> CountAsync(
        string userId, MemoryCategory? category, MemoryLifecycleState? state, Guid? projectId, bool generalOnly,
        string? query, CancellationToken cancellationToken = default)
    {
        var queryable = dbContext.Memories.Where(m => m.UserId == userId);

        if (category is not null)
        {
            queryable = queryable.Where(m => m.Category == category);
        }

        if (state is not null)
        {
            queryable = queryable.Where(m => m.State == state);
        }

        if (generalOnly)
        {
            queryable = queryable.Where(m => m.ProjectId == null);
        }
        else if (projectId is not null)
        {
            queryable = queryable.Where(m => m.ProjectId == projectId);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return await queryable.CountAsync(cancellationToken);
        }

        // Same client-side-filter-after-materialize approach as SearchAsync — Content is
        // encrypted at rest (research.md Decision 12), so free-text search cannot be pushed down.
        var candidates = await queryable.ToListAsync(cancellationToken);
        return candidates.Count(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<MemoryEntity>> GetAllByUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.Memories.Where(m => m.UserId == userId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemoryEntity>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.Memories.Where(m => m.ProjectId == projectId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemoryEntity>> GetActiveByCategoryAsync(string userId, Guid? projectId, MemoryCategory category, CancellationToken cancellationToken = default) =>
        await dbContext.Memories
            .Where(m => m.UserId == userId && m.ProjectId == projectId && m.Category == category && m.State == MemoryLifecycleState.Active)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MemoryEntity>> GetCleanupCandidatesAsync(DateTime nowUtc, DateTime staleArchivedBeforeUtc, int batchSize, CancellationToken cancellationToken = default) =>
        await dbContext.Memories
            .Where(m =>
                (m.ExpiresAtUtc != null && m.ExpiresAtUtc <= nowUtc) ||
                (m.State == MemoryLifecycleState.Archived && m.LastReinforcedAtUtc <= staleArchivedBeforeUtc))
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public void Add(MemoryEntity memory) => dbContext.Memories.Add(memory);
}
