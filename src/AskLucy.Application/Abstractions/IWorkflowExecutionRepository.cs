using AskLucy.Application.Workflows;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="WorkflowExecution"/> (constitution §3 Repository rules) — the execution and every child row (nodes/events/approvals/errors/usage/cost) it owns.</summary>
public interface IWorkflowExecutionRepository
{
    /// <summary>Full aggregate load, including nodes/events/approvals/errors/usage/cost — used by the runner and by <c>GetWorkflowExecutionQuery</c>.</summary>
    Task<WorkflowExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WorkflowExecution?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>A lightweight, untracked status read — used by the orchestrator's node-boundary loop (FR-048, SC-007) to observe a pause/cancel requested from a concurrent HTTP request without loading (or risking a concurrency conflict against) the full tracked aggregate it already holds. Null if the execution no longer exists.</summary>
    Task<WorkflowExecutionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(WorkflowExecution execution);

    /// <summary>Cursor-based (keyset) listing over the caller's own executions (contracts/workflows-api.md <c>ListWorkflowExecutionsQuery</c>).</summary>
    Task<(IReadOnlyList<WorkflowExecution> Items, string? NextCursor)> ListByUserAsync(
        string userId, Guid? workflowId, WorkflowExecutionStatus? status, WorkflowExecutionTriggerType? triggerType,
        string? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Count of the caller's executions in a non-terminal status (Queued/Running/Paused/WaitingForApproval) — backs the FR-069 concurrency cap.</summary>
    Task<int> CountActiveByUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Workflow Monitoring dashboard aggregate (spec.md User Story 8) — computed via direct aggregate queries against the caller's own executions/usage/cost rows, never by loading full execution aggregates into memory.</summary>
    Task<WorkflowStatisticsDto> GetStatisticsAsync(string userId, CancellationToken cancellationToken = default);
}
