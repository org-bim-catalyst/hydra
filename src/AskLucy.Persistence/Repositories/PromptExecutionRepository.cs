using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts;
using AskLucy.Domain.Prompts;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class PromptExecutionRepository(AskLucyDbContext dbContext) : IPromptExecutionRepository
{
    private const int MaxPageSize = 200;

    public void Add(PromptExecution execution) => dbContext.PromptExecutions.Add(execution);

    public void AddResult(PromptExecutionResult result) => dbContext.PromptExecutionResults.Add(result);

    public Task<PromptExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.PromptExecutions.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<PromptExecutionResult?> GetResultByExecutionIdAsync(Guid promptExecutionId, CancellationToken cancellationToken = default) =>
        dbContext.PromptExecutionResults.FirstOrDefaultAsync(r => r.PromptExecutionId == promptExecutionId, cancellationToken);

    public Task<PromptRating?> GetRatingByExecutionIdAsync(Guid promptExecutionId, CancellationToken cancellationToken = default) =>
        dbContext.PromptRatings.FirstOrDefaultAsync(r => r.PromptExecutionId == promptExecutionId, cancellationToken);

    public void AddRating(PromptRating rating) => dbContext.PromptRatings.Add(rating);

    public async Task<(IReadOnlyList<PromptExecution> Items, string? NextCursor)> ListForPromptAsync(
        Guid promptId, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var decoded = PromptCursor.Decode(cursor);
        var query = dbContext.PromptExecutions.Where(e => e.PromptId == promptId);

        if (decoded is { } d)
        {
            query = query.Where(e => e.CreatedAtUtc < d.SortValue || (e.CreatedAtUtc == d.SortValue && e.Id != d.Id));
        }

        var page = await query
            .OrderByDescending(e => e.CreatedAtUtc).ThenByDescending(e => e.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > pageSize;
        var items = hasMore ? page.Take(pageSize).ToList() : page;

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = PromptCursor.Encode(last.CreatedAtUtc, last.Id);
        }

        return (items, nextCursor);
    }

    public async Task<IReadOnlyList<PromptExecution>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await dbContext.PromptExecutions.Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);

    public async Task<PromptRatingBreakdownDto> GetRatingBreakdownByPromptIdAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        var counts = await (
            from rating in dbContext.PromptRatings
            join execution in dbContext.PromptExecutions on rating.PromptExecutionId equals execution.Id
            where execution.PromptId == promptId
            group rating by rating.RatingValue into g
            select new { Value = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new PromptRatingBreakdownDto(
            counts.FirstOrDefault(c => c.Value == PromptRatingValue.Good)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Value == PromptRatingValue.NeedsImprovement)?.Count ?? 0,
            counts.FirstOrDefault(c => c.Value == PromptRatingValue.Failed)?.Count ?? 0);
    }
}
