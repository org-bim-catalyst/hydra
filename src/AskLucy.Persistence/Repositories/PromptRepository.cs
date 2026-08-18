using AskLucy.Application.Abstractions;
using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptRepository(AskLucyDbContext dbContext) : IPromptRepository
{
    private const int MaxPageSize = 200;

    public Task<Prompt?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) =>
        dbContext.Prompts.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == ownerId, cancellationToken);

    public Task<Prompt?> GetByOwnerAndNameAsync(string ownerId, string name, CancellationToken cancellationToken = default) =>
        dbContext.Prompts.FirstOrDefaultAsync(p => p.OwnerId == ownerId && p.Name.ToLower() == name.ToLower(), cancellationToken);

    public void Add(Prompt prompt) => dbContext.Prompts.Add(prompt);

    public async Task<IReadOnlyList<Prompt>> ListByFolderIdAsync(Guid folderId, CancellationToken cancellationToken = default) =>
        await dbContext.Prompts.Where(p => p.FolderId == folderId).ToListAsync(cancellationToken);

    public Task<PromptVersion?> GetVersionAsync(Guid promptId, int versionNumber, CancellationToken cancellationToken = default) =>
        dbContext.PromptVersions
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.PromptId == promptId && v.VersionNumber == versionNumber, cancellationToken);

    public Task<PromptVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        dbContext.PromptVersions
            .Include(v => v.Variables)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

    public async Task<IReadOnlyList<PromptVersion>> ListVersionsAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        await dbContext.PromptVersions
            .Where(v => v.PromptId == promptId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cursor-based (keyset) search/filter over the caller's own prompts (FR-050–FR-053). Each
    /// <see cref="PromptListView"/> has one natural descending sort column — the last row's value
    /// for that column plus its id form the opaque cursor (<see cref="PromptCursor"/>), mirroring
    /// <c>ConversationCursor</c>'s single-sort-per-query-shape simplicity rather than
    /// <c>KnowledgeBaseRepository</c>'s multi-sort-parameter design, since this contract
    /// (contracts/prompts-api.md) exposes a view, not an arbitrary sort column.
    /// </summary>
    public async Task<(IReadOnlyList<Prompt> Items, string? NextCursor)> SearchAsync(
        string ownerId,
        PromptListView view,
        string? query,
        Guid? categoryId,
        string? tag,
        Guid? folderId,
        PromptStatus? status,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var baseQuery = dbContext.Prompts.Include(p => p.Tags).Where(p => p.OwnerId == ownerId);

        baseQuery = view switch
        {
            PromptListView.Favorites => baseQuery.Where(p => p.IsFavorite && p.Status != PromptStatus.Archived),
            PromptListView.Pinned => baseQuery.Where(p => p.IsPinned && p.Status != PromptStatus.Archived),
            PromptListView.Archived => baseQuery.Where(p => p.Status == PromptStatus.Archived),
            _ => baseQuery.Where(p => p.Status != PromptStatus.Archived),
        };

        if (status is not null)
        {
            baseQuery = baseQuery.Where(p => p.Status == status);
        }

        if (categoryId is not null)
        {
            baseQuery = baseQuery.Where(p => p.CategoryId == categoryId);
        }

        if (folderId is not null)
        {
            baseQuery = baseQuery.Where(p => p.FolderId == folderId);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            baseQuery = baseQuery.Where(p => dbContext.PromptTags.Any(t => t.PromptId == p.Id && t.Value == tag));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            baseQuery = baseQuery.Where(p =>
                EF.Functions.Contains(p.Name, query) ||
                EF.Functions.Contains(p.UserInstructions, query) ||
                (p.Description != null && EF.Functions.Contains(p.Description, query)) ||
                (p.SystemInstructions != null && EF.Functions.Contains(p.SystemInstructions, query)));
        }

        return view switch
        {
            PromptListView.RecentlyUsed => await FetchByRecentlyUsedAsync(baseQuery, cursor, pageSize, cancellationToken),
            _ => await FetchByModifiedAsync(baseQuery, cursor, pageSize, cancellationToken),
        };
    }

    private async Task<(IReadOnlyList<Prompt> Items, string? NextCursor)> FetchByModifiedAsync(
        IQueryable<Prompt> baseQuery, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var decoded = PromptCursor.Decode(cursor);
        var sorted = baseQuery
            .Select(p => new { Prompt = p, SortValue = p.ModifiedAtUtc ?? p.CreatedAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Prompt.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Prompt.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Prompt.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        return BuildPage(page, pageSize, x => x.Prompt, x => x.SortValue);
    }

    private async Task<(IReadOnlyList<Prompt> Items, string? NextCursor)> FetchByRecentlyUsedAsync(
        IQueryable<Prompt> baseQuery, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var decoded = PromptCursor.Decode(cursor);
        var sorted =
            from p in baseQuery
            join s in dbContext.PromptUsageStatistics on p.Id equals s.PromptId
            where s.LastSuccessfulUseAtUtc != null
            select new { Prompt = p, SortValue = s.LastSuccessfulUseAtUtc!.Value };

        var ordered = sorted.OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Prompt.Id);

        if (decoded is { } d)
        {
            ordered = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Prompt.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Prompt.Id);
        }

        var page = await ordered.Take(pageSize + 1).ToListAsync(cancellationToken);
        return BuildPage(page, pageSize, x => x.Prompt, x => x.SortValue);
    }

    private static (IReadOnlyList<Prompt> Items, string? NextCursor) BuildPage<T>(
        List<T> page, int pageSize, Func<T, Prompt> selectPrompt, Func<T, DateTime> selectSortValue)
    {
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = PromptCursor.Encode(selectSortValue(last), selectPrompt(last).Id);
        }

        return (items.Select(selectPrompt).ToList(), nextCursor);
    }

    public async Task<IReadOnlyList<string>> ListDistinctTagValuesAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await dbContext.PromptTags
            .Where(t => t.OwnerId == ownerId)
            .Select(t => t.Value)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync(cancellationToken);

    public Task<PromptUsageStatistics?> GetUsageStatisticsAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        dbContext.PromptUsageStatistics.FirstOrDefaultAsync(s => s.PromptId == promptId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, PromptUsageStatistics>> GetUsageStatisticsByPromptIdsAsync(
        IReadOnlyCollection<Guid> promptIds, CancellationToken cancellationToken = default) =>
        await dbContext.PromptUsageStatistics
            .Where(s => promptIds.Contains(s.PromptId))
            .ToDictionaryAsync(s => s.PromptId, cancellationToken);

    public void AddUsageStatistics(PromptUsageStatistics statistics) => dbContext.PromptUsageStatistics.Add(statistics);
}
