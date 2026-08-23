using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AgentExecutionRepository(AskLucyDbContext dbContext) : IAgentExecutionRepository
{
    private const int MaxPageSize = 200;

    private static readonly AgentExecutionStatus[] ActiveStatuses =
    [
        AgentExecutionStatus.Running, AgentExecutionStatus.Paused, AgentExecutionStatus.WaitingForApproval,
    ];

    public Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AgentExecutions
            .Include(e => e.Steps)
            .Include(e => e.Events)
            .Include(e => e.Approvals)
            .Include(e => e.Errors)
            .Include(e => e.Usage)
            .Include(e => e.Cost)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<AgentExecution?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default) =>
        dbContext.AgentExecutions
            .Include(e => e.Steps)
            .Include(e => e.Events)
            .Include(e => e.Approvals)
            .Include(e => e.Errors)
            .Include(e => e.Usage)
            .Include(e => e.Cost)
            .FirstOrDefaultAsync(e => e.Id == id && e.RunByUserId == userId, cancellationToken);

    public void Add(AgentExecution execution) => dbContext.AgentExecutions.Add(execution);

    public Task<AgentExecutionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AgentExecutions.AsNoTracking().Where(e => e.Id == id).Select(e => (AgentExecutionStatus?)e.Status).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<AgentExecution> Items, string? NextCursor)> ListByUserAsync(
        string userId, Guid? agentId, AgentExecutionStatus? status, bool? isTestExecution,
        string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.AgentExecutions.Where(e => e.RunByUserId == userId);

        if (agentId is not null)
        {
            query = query.Where(e => e.AgentId == agentId);
        }

        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        if (isTestExecution is not null)
        {
            query = query.Where(e => e.IsTestExecution == isTestExecution);
        }

        var decoded = AgentCursor.Decode(cursor);
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
            nextCursor = AgentCursor.Encode(last.SortValue, last.Execution.Id);
        }

        return (items.Select(x => x.Execution).ToList(), nextCursor);
    }

    public Task<int> CountActiveByUserAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.AgentExecutions.CountAsync(e => e.RunByUserId == userId && ActiveStatuses.Contains(e.Status), cancellationToken);

    public Task<AgentToolCall?> GetToolCallByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AgentToolCalls.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AgentToolCall>> ListToolCallsByStepIdsAsync(IReadOnlyCollection<Guid> stepIds, CancellationToken cancellationToken = default) =>
        await dbContext.AgentToolCalls.Where(t => stepIds.Contains(t.AgentExecutionStepId)).ToListAsync(cancellationToken);

    public void AddToolCall(AgentToolCall toolCall) => dbContext.AgentToolCalls.Add(toolCall);

    public async Task<IReadOnlyList<AgentExecutionStep>> ListCompletedStepsByUserChatIdAsync(Guid userChatId, CancellationToken cancellationToken = default) =>
        await dbContext.AgentExecutionSteps
            .Where(s => s.Status == AgentExecutionStepStatus.Completed && dbContext.AgentExecutions
                .Where(e => e.UserChatId == userChatId)
                .Select(e => e.Id)
                .Contains(s.AgentExecutionId))
            .OrderByDescending(s => s.CompletedAtUtc)
            .ToListAsync(cancellationToken);
}
