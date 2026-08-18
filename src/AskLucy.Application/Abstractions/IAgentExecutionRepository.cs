using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="AgentExecution"/> (constitution §3 Repository rules) — the execution and every child row (steps/events/tool calls/approvals/errors/usage/cost) it owns.</summary>
public interface IAgentExecutionRepository
{
    /// <summary>Full aggregate load, including steps/events/approvals/errors/usage/cost — used by the runner and by <c>GetAgentExecutionQuery</c>.</summary>
    Task<AgentExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AgentExecution?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>A lightweight, untracked status read — used by the orchestrator's step-boundary loop (FR-017, SC-009) to observe a pause/cancel requested from a concurrent HTTP request without loading (or risking a concurrency conflict against) the full tracked aggregate it already holds. Null if the execution no longer exists.</summary>
    Task<AgentExecutionStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(AgentExecution execution);

    /// <summary>Cursor-based (keyset) listing over the caller's own executions (contracts/agents-api.md <c>ListAgentExecutionsQuery</c>).</summary>
    Task<(IReadOnlyList<AgentExecution> Items, string? NextCursor)> ListByUserAsync(
        string userId, Guid? agentId, AgentExecutionStatus? status, bool? isTestExecution,
        string? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Count of the caller's executions in a non-terminal status (Running/Paused/WaitingForApproval) — backs the FR-042 concurrency cap.</summary>
    Task<int> CountActiveByUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<AgentToolCall?> GetToolCallByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every tool call across the given steps — used for duplicate-call detection (FR-039) across the *whole* execution's history, not just the current run-loop iteration, so it stays correct across a pause/resume (FR-017).</summary>
    Task<IReadOnlyList<AgentToolCall>> ListToolCallsByStepIdsAsync(IReadOnlyCollection<Guid> stepIds, CancellationToken cancellationToken = default);

    void AddToolCall(AgentToolCall toolCall);
}
