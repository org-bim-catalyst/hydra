using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Queries.SearchUserChats;
using AskLucy.Domain.Chats;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserChatRepository(AskLucyDbContext dbContext) : IUserChatRepository
{
    public Task<UserChat?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.UserChats.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<UserChat?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.UserChats.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task PurgeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await dbContext.UserChats.IgnoreQueryFilters().Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<UserChat>> ListByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await dbContext.UserChats
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(UserChat chat) => dbContext.UserChats.Add(chat);

    public async Task<IReadOnlyList<UserChat>> ListNeedingMemoryAnalysisAsync(int batchSize, CancellationToken cancellationToken = default) =>
        await dbContext.UserChats
            .Where(c => c.LastMemoryAnalyzedAtUtc == null || c.ModifiedAtUtc > c.LastMemoryAnalyzedAtUtc)
            .OrderBy(c => c.ModifiedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cursor-based (keyset) search/filter/sort over a user's own conversations
    /// (FR-019/FR-020/FR-021/FR-022, research.md Topics 5/6). Pinned conversations always
    /// sort ahead of unpinned ones (FR-008) — modeled as an ascending "pin rank" (0 = pinned)
    /// that is always the first ORDER BY/cursor key, regardless of <paramref name="sort"/>.
    /// Four concrete branches (one per sort) rather than one generically-typed method: EF
    /// Core needs a statically-typed, translatable key per branch (DateTime vs. string), and
    /// a dynamic-LINQ abstraction over four call sites would be premature generality
    /// (constitution §2.III YAGNI).
    /// </summary>
    public async Task<(IReadOnlyList<UserChat> Items, string? NextCursor)> SearchAsync(
        string userId,
        ConversationView view,
        bool? pinnedOnly,
        bool? favoriteOnly,
        string? query,
        ConversationSort sort,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var bypassSoftDeleteFilter = view is ConversationView.Deleted or ConversationView.All;
        IQueryable<UserChat> baseQuery = bypassSoftDeleteFilter
            ? dbContext.UserChats.IgnoreQueryFilters().Where(c => c.UserId == userId)
            : dbContext.UserChats.Where(c => c.UserId == userId);

        baseQuery = view switch
        {
            ConversationView.Active => baseQuery.Where(c => c.DeletedAtUtc == null && c.ArchivedAtUtc == null),
            ConversationView.Archived => baseQuery.Where(c => c.DeletedAtUtc == null && c.ArchivedAtUtc != null),
            ConversationView.Deleted => baseQuery.Where(c => c.DeletedAtUtc != null),
            _ => baseQuery,
        };

        if (pinnedOnly == true)
        {
            baseQuery = baseQuery.Where(c => c.PinnedAtUtc != null);
        }

        if (favoriteOnly == true)
        {
            baseQuery = baseQuery.Where(c => c.IsFavorite);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var messages = dbContext.Messages.IgnoreQueryFilters().AsQueryable();
            baseQuery = baseQuery.Where(c =>
                EF.Functions.Contains(c.Title, query) ||
                messages.Any(m => m.UserChatId == c.Id && EF.Functions.Contains(m.Content, query)));
        }

        var decodedCursor = ConversationCursor.Decode(cursor);

        return sort switch
        {
            ConversationSort.Oldest => await FetchByCreatedAtAsync(baseQuery, decodedCursor, pageSize, ascending: true, cancellationToken),
            ConversationSort.RecentlyUpdated => await FetchByLastActivityAsync(baseQuery, decodedCursor, pageSize, cancellationToken),
            ConversationSort.Alphabetical => await FetchByTitleAsync(baseQuery, decodedCursor, pageSize, cancellationToken),
            _ => await FetchByCreatedAtAsync(baseQuery, decodedCursor, pageSize, ascending: false, cancellationToken),
        };
    }

    private static async Task<(IReadOnlyList<UserChat>, string?)> FetchByCreatedAtAsync(
        IQueryable<UserChat> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, int pageSize, bool ascending, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = ConversationCursor.DecodeDateTime(c.SortValue);
            var cursorId = c.Id;

            query = ascending
                ? query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.CreatedAtUtc > cursorValue || (x.CreatedAtUtc == cursorValue && x.Id > cursorId))))
                : query.Where(x =>
                    (cursorPinned && x.PinnedAtUtc == null) ||
                    ((x.PinnedAtUtc != null) == cursorPinned &&
                        (x.CreatedAtUtc < cursorValue || (x.CreatedAtUtc == cursorValue && x.Id < cursorId))));
        }

        var pinnedFirst = query.OrderBy(x => x.PinnedAtUtc == null);
        var ordered = ascending
            ? pinnedFirst.ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            : pinnedFirst.ThenByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => ConversationCursor.EncodeDateTime(last.CreatedAtUtc));
    }

    private static async Task<(IReadOnlyList<UserChat>, string?)> FetchByLastActivityAsync(
        IQueryable<UserChat> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = ConversationCursor.DecodeDateTime(c.SortValue);
            var cursorId = c.Id;

            query = query.Where(x =>
                (cursorPinned && x.PinnedAtUtc == null) ||
                ((x.PinnedAtUtc != null) == cursorPinned &&
                    ((x.ModifiedAtUtc ?? x.CreatedAtUtc) < cursorValue ||
                        ((x.ModifiedAtUtc ?? x.CreatedAtUtc) == cursorValue && x.Id < cursorId))));
        }

        var ordered = query
            .OrderBy(x => x.PinnedAtUtc == null)
            .ThenByDescending(x => x.ModifiedAtUtc ?? x.CreatedAtUtc)
            .ThenByDescending(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => ConversationCursor.EncodeDateTime(last.ModifiedAtUtc ?? last.CreatedAtUtc));
    }

    private static async Task<(IReadOnlyList<UserChat>, string?)> FetchByTitleAsync(
        IQueryable<UserChat> baseQuery, (int Rank, string SortValue, Guid Id)? cursor, int pageSize, CancellationToken ct)
    {
        var query = baseQuery;

        if (cursor is { } c)
        {
            var cursorPinned = c.Rank == 0;
            var cursorValue = c.SortValue;
            var cursorId = c.Id;

            query = query.Where(x =>
                (cursorPinned && x.PinnedAtUtc == null) ||
                ((x.PinnedAtUtc != null) == cursorPinned &&
                    (x.Title.CompareTo(cursorValue) > 0 ||
                        (x.Title.CompareTo(cursorValue) == 0 && x.Id > cursorId))));
        }

        var ordered = query
            .OrderBy(x => x.PinnedAtUtc == null)
            .ThenBy(x => x.Title)
            .ThenBy(x => x.Id);

        var page = await ordered.Take(pageSize + 1).ToListAsync(ct);
        return BuildPage(page, pageSize, last => last.Title);
    }

    private static (IReadOnlyList<UserChat>, string?) BuildPage(
        List<UserChat> page, int pageSize, Func<UserChat, string> encodeSortValue)
    {
        var hasNextPage = page.Count > pageSize;
        var items = hasNextPage ? page.GetRange(0, pageSize) : page;

        string? nextCursor = null;
        if (hasNextPage && items.Count > 0)
        {
            var last = items[^1];
            var rank = last.PinnedAtUtc is null ? 1 : 0;
            nextCursor = ConversationCursor.Encode(rank, encodeSortValue(last), last.Id);
        }

        return (items, nextCursor);
    }
}
