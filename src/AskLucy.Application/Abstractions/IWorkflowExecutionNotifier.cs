namespace AskLucy.Application.Abstractions;

/// <summary>
/// Live push of a workflow execution's progress to the caller's own browser session (spec.md
/// FR-049, contracts/workflow-execution-events.md, research.md Decision 9). The concrete delivery
/// mechanism is a SignalR hub (<c>WorkflowExecutionHub</c>) implemented in Infrastructure —
/// Application/Domain never reference SignalR directly (constitution §3). Every payload mirrors an
/// already-persisted <see cref="Domain.Workflows.WorkflowExecutionEvent"/> row (never raw node
/// input/output content, FR-053) so a client that misses a live push can always reconcile via the
/// REST events endpoint. Method names mirror <see cref="Domain.Workflows.WorkflowExecutionEventType"/>'s
/// literal naming exactly (e.g. <see cref="NotifyWorkflowStartedAsync"/>/<see cref="NotifyNodeStartedAsync"/>)
/// — a <c>Workflow*</c>/<c>Node*</c> split, not Agent Runtime's <c>Execution*</c> prefix.
/// </summary>
public interface IWorkflowExecutionNotifier
{
    Task NotifyWorkflowStartedAsync(string userId, Guid executionId, Guid workflowId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyNodeStartedAsync(string userId, Guid executionId, Guid workflowNodeId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyNodeCompletedAsync(string userId, Guid executionId, Guid workflowNodeId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyNodeFailedAsync(string userId, Guid executionId, Guid workflowNodeId, string errorMessage, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyNodeRetryingAsync(string userId, Guid executionId, Guid workflowNodeId, int retryCount, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalRequestedAsync(string userId, Guid executionId, Guid workflowNodeId, Guid approvalId, string intendedActionDescription, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalGrantedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, bool wasPolicyBased, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyApprovalRejectedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyWorkflowPausedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyWorkflowResumedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyWorkflowCompletedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyWorkflowFailedAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyWorkflowCancelledAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default);

    Task NotifyUsageUpdatedAsync(string userId, Guid executionId, int? inputTokenCount, int? outputTokenCount, decimal? estimatedCost, CancellationToken cancellationToken = default);
}
