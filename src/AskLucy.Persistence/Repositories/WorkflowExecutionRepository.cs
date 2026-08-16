using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows;
using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class WorkflowExecutionRepository(AskLucyDbContext dbContext) : IWorkflowExecutionRepository
{
    private const int MaxPageSize = 200;

    private static readonly WorkflowExecutionStatus[] ActiveStatuses =
    [
        WorkflowExecutionStatus.Queued, WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Paused, WorkflowExecutionStatus.WaitingForApproval,
    ];

    private static readonly WorkflowExecutionStatus[] InProgressStatuses =
    [
        WorkflowExecutionStatus.Running, WorkflowExecutionStatus.Paused, WorkflowExecutionStatus.WaitingForApproval,
    ];

    private static readonly WorkflowExecutionStatus[] FailedLikeStatuses =
    [
        WorkflowExecutionStatus.Failed, WorkflowExecutionStatus.Cancelled, WorkflowExecutionStatus.TimedOut,
    ];

    public Task<WorkflowExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowExecutions
            .Include(e => e.Nodes)
            .Include(e => e.Events)
            .Include(e => e.Approvals)
            .Include(e => e.Errors)
            .Include(e => e.Usage)
            .Include(e => e.Cost)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<WorkflowExecution?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowExecutions
            .Include(e => e.Nodes)
            .Include(e => e.Events)
            .Include(e => e.Approvals)
            .Include(e => e.Errors)
            .Include(e => e.Usage)
            .Include(e => e.Cost)
            .FirstOrDefaultAsync(e => e.Id == id && e.RunByUserId == userId, cancellationToken);

    public void Add(WorkflowExecution execution) => dbContext.WorkflowExecutions.Add(execution);

    public Task<WorkflowExecutionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowExecutions.AsNoTracking().Where(e => e.Id == id).Select(e => (WorkflowExecutionStatus?)e.Status).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<WorkflowExecution> Items, string? NextCursor)> ListByUserAsync(
        string userId, Guid? workflowId, WorkflowExecutionStatus? status, WorkflowExecutionTriggerType? triggerType,
        string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.WorkflowExecutions.Where(e => e.RunByUserId == userId);

        if (workflowId is not null)
        {
            query = query.Where(e => e.WorkflowId == workflowId);
        }

        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        if (triggerType is not null)
        {
            query = query.Where(e => e.TriggerType == triggerType);
        }

        var decoded = WorkflowCursor.Decode(cursor);
        var sorted = query
            .Select(e => new { Execution = e, SortValue = e.CreatedAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Execution.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Execution.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Execution.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = WorkflowCursor.Encode(last.SortValue, last.Execution.Id);
        }

        return (items.Select(x => x.Execution).ToList(), nextCursor);
    }

    public Task<int> CountActiveByUserAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowExecutions.CountAsync(e => e.RunByUserId == userId && ActiveStatuses.Contains(e.Status), cancellationToken);

    public async Task<WorkflowStatisticsDto> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var ownExecutions = dbContext.WorkflowExecutions.Where(e => e.RunByUserId == userId);

        var activeCount = await ownExecutions.CountAsync(e => InProgressStatuses.Contains(e.Status), cancellationToken);
        var queuedCount = await ownExecutions.CountAsync(e => e.Status == WorkflowExecutionStatus.Queued, cancellationToken);
        var failedCount = await ownExecutions.CountAsync(e => FailedLikeStatuses.Contains(e.Status), cancellationToken);
        var completedCount = await ownExecutions.CountAsync(e => e.Status == WorkflowExecutionStatus.Completed, cancellationToken);

        var terminalDurations = await ownExecutions
            .Where(e => e.StartedAtUtc != null && e.CompletedAtUtc != null)
            .Select(e => new { e.StartedAtUtc, e.CompletedAtUtc })
            .ToListAsync(cancellationToken);
        double? averageDurationSeconds = terminalDurations.Count == 0
            ? null
            : terminalDurations.Average(d => (d.CompletedAtUtc!.Value - d.StartedAtUtc!.Value).TotalSeconds);

        var decidedCount = failedCount + completedCount;
        var failureRate = decidedCount == 0 ? 0d : (double)failedCount / decidedCount;

        var totalInputTokens = await ownExecutions.SumAsync(e => e.Usage != null ? (e.Usage.InputTokenCount ?? 0) : 0, cancellationToken);
        var totalOutputTokens = await ownExecutions.SumAsync(e => e.Usage != null ? (e.Usage.OutputTokenCount ?? 0) : 0, cancellationToken);
        var totalEstimatedCost = await ownExecutions.SumAsync(e => e.Cost != null ? e.Cost.EstimatedCost : 0m, cancellationToken);

        return new WorkflowStatisticsDto(
            activeCount, queuedCount, failedCount, completedCount, averageDurationSeconds, failureRate,
            totalInputTokens, totalOutputTokens, totalEstimatedCost);
    }
}
