using AskLucy.Domain.Agents;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Live push of an execution's progress to the caller's own browser session (spec.md FR-034,
/// contracts/agent-execution-events.md, research.md Decision 9). The concrete delivery mechanism
/// is a SignalR hub (<c>AgentExecutionHub</c>) implemented in <c>Infrastructure</c> — Application/
/// Domain never reference SignalR directly (constitution §3). Every payload mirrors an already-
/// persisted <see cref="AgentExecutionEvent"/> row (never private chain-of-thought or raw
/// provider/tool payloads, FR-035) so a client that misses a live push can always reconcile via
/// the REST events endpoint.
/// </summary>
public interface IAgentExecutionNotifier
{
    Task NotifyExecutionStartedAsync(string userId, Guid executionId, Guid agentId, int agentVersionNumber, string objective, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyPlanCreatedAsync(string userId, Guid executionId, int stepCount, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyStepStartedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyStepCompletedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyStepFailedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, string errorMessage, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyToolCallStartedAsync(string userId, Guid executionId, Guid stepId, string toolName, AgentToolRiskLevel riskLevel, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyToolCallCompletedAsync(string userId, Guid executionId, Guid stepId, string toolName, AgentToolRiskLevel riskLevel, bool succeeded, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalRequestedAsync(string userId, Guid executionId, Guid approvalId, string intendedActionDescription, AgentToolRiskLevel riskLevel, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalGrantedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, bool wasPolicyBased, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalRejectedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyExecutionCompletedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyExecutionFailedAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyExecutionCancelledAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyUsageUpdatedAsync(string userId, Guid executionId, int? inputTokenCount, int? outputTokenCount, decimal? estimatedCost, CancellationToken cancellationToken = default);
}
