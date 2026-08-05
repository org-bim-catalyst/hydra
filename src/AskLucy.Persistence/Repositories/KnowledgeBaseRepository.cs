using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Domain.KnowledgeBases;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class KnowledgeBaseRepository(AskLucyDbContext dbContext) : IKnowledgeBaseRepository
{
    public Task<KnowledgeBase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.KnowledgeBases.Include(k => k.Tags).FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public Task<IReadOnlyList<KnowledgeBase>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        ids.Count == 0
            ? Task.FromResult<IReadOnlyList<KnowledgeBase>>([])
            : GetByIdsCoreAsync(ids, cancellationToken);

    private async Task<IReadOnlyList<KnowledgeBase>> GetByIdsCoreAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await dbContext.KnowledgeBases.Where(k => ids.Contains(k.Id)).ToListAsync(cancellationToken);

    public Task<KnowledgeBase?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.KnowledgeBases.IgnoreQueryFilters().Include(k => k.Tags).FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

    public void Add(KnowledgeBase knowledgeBase) => dbContext.KnowledgeBases.Add(knowledgeBase);

    public async Task PurgeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBases.IgnoreQueryFilters().Where(k => k.Id == id).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<KnowledgeBase>> ListPastPurgeScheduleAsync(DateTime asOfUtc, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBases.IgnoreQueryFilters()
            .Where(k => k.DeletedAtUtc != null && k.PurgeScheduledAtUtc != null && k.PurgeScheduledAtUtc <= asOfUtc)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cursor-based (keyset) search/filter/sort over a user's own knowledge bases (FR-022–
    /// FR-024), mirrors <c>UserChatRepository.SearchAsync</c>. Five concrete sort branches
    /// rather than one generically-typed method: EF Core needs a statically-typed,
    /// translatable key per branch (string vs. DateTime vs. int vs. long), and a dynamic-LINQ
    /// abstraction over five call sites would be premature generality (constitution §2.III
    /// YAGNI).
    /// </summary>
    public async Task<(IReadOnlyList<KnowledgeBase> Items, string? NextCursor)> SearchAsync(
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
        CancellationToken cancellationToken = default)
    {
        var baseQuery = view == KnowledgeBaseListView.Deleted
            ? dbContext.KnowledgeBases.IgnoreQueryFilters().Where(k => k.OwnerId == ownerId && k.DeletedAtUtc != null)
            : dbContext.KnowledgeBases.Where(k => k.OwnerId == ownerId);

        baseQuery = view switch
        {
            KnowledgeBaseListView.Active => baseQuery.Where(k => k.Status != KnowledgeBaseStatus.Archived),
            KnowledgeBaseListView.Archived => baseQuery.Where(k => k.Status == KnowledgeBaseStatus.Archived),
            _ => baseQuery,
        };

        if (categoryId is not null)
        {
            baseQuery = baseQuery.Where(k => k.CategoryId == categoryId);
        }

        if (favoriteOnly == true)
        {
            baseQuery = baseQuery.Where(k => k.IsFavorite);
        }

        if (pinnedOnly == true)
        {
            baseQuery = baseQuery.Where(k => k.PinnedAtUtc != null);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            baseQuery = baseQuery.Where(k => k.Tags.Any(t => t.Value == tag));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            // FR-022: name/description/tags only — not owner or dates, which are sort-only
            // (spec.md's `/speckit-analyze` remediation, contracts/knowledge-bases-api.md).
            baseQuery = baseQuery.Where(k =>
                EF.Functions.Like(k.Name, $"%{query}%") ||
                (k.Description != null && EF.Functions.Like(k.Description, $"%{query}%")) ||
                k.Tags.Any(t => EF.Functions.Like(t.Value, $"%{query}%")));
        }

        baseQuery = baseQuery.Include(k => k.Tags);
        var decodedCursor = KnowledgeBaseCursor.Decode(cursor);

        return sort switch
        {
            KnowledgeBaseSort.Name => await FetchByNameAsync(baseQuery, decodedCursor, sortDescending, pageSize, cancellationToken),
            KnowledgeBaseSort.Created => await FetchByCreatedAtAsync(baseQuery, decodedCursor, sortDescending, pageSize, cancellationToken),
            KnowledgeBaseSort.DocumentCount => await FetchByDocumentCountAsync(baseQuery, decodedCursor, sortDescending, pageSize, cancellationToken),
            KnowledgeBaseSort.StorageSize => await FetchByStorageSizeAsync(baseQuery, decodedCursor, sortDescending, pageSize, cancellationToken),
            _ => await FetchByRecentlyUpdatedAsync(baseQuery, decodedCursor, pageSize, cancellationToken),
        };
    }

    private static async Task<(IReadOnlyList<KnowledgeBase>, string?)> FetchByNameAsync(
        IQueryable<KnowledgeBase> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, bool descending, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            query = descending
                ? query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.Name.CompareTo(c.SortValue) < 0 || (x.Name == c.SortValue && x.Id < c.Id))))
                : query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.Name.CompareTo(c.SortValue) > 0 || (x.Name == c.SortValue && x.Id > c.Id))));
        }

        var pinnedFirst = query.OrderBy(x => x.PinnedAtUtc == null);
        var ordered = descending ? pinnedFirst.ThenByDescending(x => x.Name).ThenByDescending(x => x.Id)
            : pinnedFirst.ThenBy(x => x.Name).ThenBy(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => last.Name);
    }

    private static async Task<(IReadOnlyList<KnowledgeBase>, string?)> FetchByCreatedAtAsync(
        IQueryable<KnowledgeBase> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, bool descending, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = KnowledgeBaseCursor.DecodeDateTime(c.SortValue);
            query = descending
                ? query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.CreatedAtUtc < cursorValue || (x.CreatedAtUtc == cursorValue && x.Id < c.Id))))
                : query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.CreatedAtUtc > cursorValue || (x.CreatedAtUtc == cursorValue && x.Id > c.Id))));
        }

        var pinnedFirst = query.OrderBy(x => x.PinnedAtUtc == null);
        var ordered = descending ? pinnedFirst.ThenByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            : pinnedFirst.ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => KnowledgeBaseCursor.EncodeDateTime(last.CreatedAtUtc));
    }

    private static async Task<(IReadOnlyList<KnowledgeBase>, string?)> FetchByRecentlyUpdatedAsync(
        IQueryable<KnowledgeBase> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = KnowledgeBaseCursor.DecodeDateTime(c.SortValue);
            query = query.Where(x =>
                (cursorPinned && x.PinnedAtUtc == null) ||
                ((x.PinnedAtUtc != null) == cursorPinned &&
                    ((x.ModifiedAtUtc ?? x.CreatedAtUtc) < cursorValue ||
                        ((x.ModifiedAtUtc ?? x.CreatedAtUtc) == cursorValue && x.Id < c.Id))));
        }

        var ordered = query
            .OrderBy(x => x.PinnedAtUtc == null)
            .ThenByDescending(x => x.ModifiedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => KnowledgeBaseCursor.EncodeDateTime(last.ModifiedAtUtc ?? last.CreatedAtUtc));
    }

    private static async Task<(IReadOnlyList<KnowledgeBase>, string?)> FetchByDocumentCountAsync(
        IQueryable<KnowledgeBase> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, bool descending, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = KnowledgeBaseCursor.DecodeInt(c.SortValue);
            query = descending
                ? query.Where(x => (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned && (x.DocumentCount < cursorValue || (x.DocumentCount == cursorValue && x.Id < c.Id))))
                : query.Where(x => (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned && (x.DocumentCount > cursorValue || (x.DocumentCount == cursorValue && x.Id > c.Id))));
        }

        var pinnedFirst = query.OrderBy(x => x.PinnedAtUtc == null);
        var ordered = descending ? pinnedFirst.ThenByDescending(x => x.DocumentCount).ThenByDescending(x => x.Id)
            : pinnedFirst.ThenBy(x => x.DocumentCount).ThenBy(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => KnowledgeBaseCursor.EncodeInt(last.DocumentCount));
    }

    private static async Task<(IReadOnlyList<KnowledgeBase>, string?)> FetchByStorageSizeAsync(
        IQueryable<KnowledgeBase> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, bool descending, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = KnowledgeBaseCursor.DecodeLong(c.SortValue);
            query = descending
                ? query.Where(x => (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned && (x.StorageSizeBytes < cursorValue || (x.StorageSizeBytes == cursorValue && x.Id < c.Id))))
                : query.Where(x => (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned && (x.StorageSizeBytes > cursorValue || (x.StorageSizeBytes == cursorValue && x.Id > c.Id))));
        }

        var pinnedFirst = query.OrderBy(x => x.PinnedAtUtc == null);
        var ordered = descending ? pinnedFirst.ThenByDescending(x => x.StorageSizeBytes).ThenByDescending(x => x.Id)
            : pinnedFirst.ThenBy(x => x.StorageSizeBytes).ThenBy(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => KnowledgeBaseCursor.EncodeLong(last.StorageSizeBytes));
    }

    private static (IReadOnlyList<KnowledgeBase>, string?) BuildPage(
        List<KnowledgeBase> page, int pageSize, Func<KnowledgeBase, string> encodeSortValue)
    {
        var hasNextPage = page.Count > pageSize;
        var items = hasNextPage ? page.GetRange(0, pageSize) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            var rank = last.PinnedAtUtc is null ? 1 : 0;
            nextCursor = KnowledgeBaseCursor.Encode(rank, encodeSortValue(last), last.Id);
        }

        return (items, nextCursor);
    }

    public async Task<KnowledgeBaseDashboardSummaryDto> GetDashboardSummaryAsync(string ownerId, DateTime recentSinceUtc, CancellationToken cancellationToken = default)
    {
        var active = dbContext.KnowledgeBases.Where(k => k.OwnerId == ownerId && k.Status != KnowledgeBaseStatus.Archived);

        var totalKnowledgeBases = await active.CountAsync(cancellationToken);
        var totalDocuments = await active.SumAsync(k => k.DocumentCount, cancellationToken);
        var totalStorageBytes = await active.SumAsync(k => k.StorageSizeBytes, cancellationToken);
        var recentCount = await active.CountAsync(k => (k.ModifiedAtUtc ?? k.CreatedAtUtc) >= recentSinceUtc, cancellationToken);
        var favoritesCount = await active.CountAsync(k => k.IsFavorite, cancellationToken);
        var pinnedCount = await active.CountAsync(k => k.PinnedAtUtc != null, cancellationToken);
        var archivedCount = await dbContext.KnowledgeBases
            .CountAsync(k => k.OwnerId == ownerId && k.Status == KnowledgeBaseStatus.Archived, cancellationToken);

        return new KnowledgeBaseDashboardSummaryDto(
            totalKnowledgeBases, totalDocuments, totalStorageBytes, recentCount, favoritesCount, pinnedCount, archivedCount);
    }

    public async Task<IReadOnlyList<string>> ListDistinctTagValuesAsync(string ownerId, string? prefix, CancellationToken cancellationToken = default)
    {
        var query = dbContext.KnowledgeBaseTags.Where(t => t.OwnerId == ownerId);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            query = query.Where(t => t.Value.StartsWith(prefix));
        }

        return await query.Select(t => t.Value).Distinct().OrderBy(v => v).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeBase>> ListByCategoryIdAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await dbContext.KnowledgeBases.Include(k => k.Tags).Where(k => k.CategoryId == categoryId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ResolveOwnedIdsAsync(
        string ownerId, IReadOnlyCollection<Guid>? candidateIds, IReadOnlyCollection<Guid>? excludeIds, CancellationToken cancellationToken = default)
    {
        var query = dbContext.KnowledgeBases.Where(k => k.OwnerId == ownerId).Select(k => k.Id);

        if (candidateIds is { Count: > 0 })
        {
            query = query.Where(id => candidateIds.Contains(id));
        }

        if (excludeIds is { Count: > 0 })
        {
            query = query.Where(id => !excludeIds.Contains(id));
        }

        return await query.ToListAsync(cancellationToken);
    }
}
