using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Workflows;

/// <summary>Pure push over <see cref="WorkflowExecutionHub"/> (contracts/workflow-execution-events.md) — persistence of the corresponding <c>WorkflowExecutionEvent</c> row happens separately in the orchestrator/command handlers via <c>WorkflowExecution.RecordEvent</c>, mirroring <c>AgentExecutionNotifier</c>'s push-only shape.</summary>
public sealed class WorkflowExecutionNotifier(IHubContext<WorkflowExecutionHub> hubContext) : IWorkflowExecutionNotifier
{
    public Task NotifyWorkflowStartedAsync(string userId, Guid executionId, Guid workflowId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowStarted", new { executionId, workflowId, occurredAtUtc }, cancellationToken);

    public Task NotifyNodeStartedAsync(string userId, Guid executionId, Guid workflowNodeId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "nodeStarted", new { executionId, workflowNodeId, status = "Running", occurredAtUtc }, cancellationToken);

    public Task NotifyNodeCompletedAsync(string userId, Guid executionId, Guid workflowNodeId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "nodeCompleted", new { executionId, workflowNodeId, status = "Completed", occurredAtUtc }, cancellationToken);

    public Task NotifyNodeFailedAsync(string userId, Guid executionId, Guid workflowNodeId, string errorMessage, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "nodeFailed", new { executionId, workflowNodeId, status = "Failed", occurredAtUtc, errorMessage }, cancellationToken);

    public Task NotifyNodeRetryingAsync(string userId, Guid executionId, Guid workflowNodeId, int retryCount, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "nodeRetrying", new { executionId, workflowNodeId, retryCount, occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalRequestedAsync(string userId, Guid executionId, Guid workflowNodeId, Guid approvalId, string intendedActionDescription, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalRequested", new { executionId, workflowNodeId, approvalId, intendedActionDescription, occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalGrantedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, bool wasPolicyBased, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalGranted", new { executionId, approvalId, decidedByUserId, wasPolicyBased, occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalRejectedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalRejected", new { executionId, approvalId, decidedByUserId, wasPolicyBased = false, occurredAtUtc }, cancellationToken);

    public Task NotifyWorkflowPausedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowPaused", new { executionId, status = "Paused", occurredAtUtc }, cancellationToken);

    public Task NotifyWorkflowResumedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowResumed", new { executionId, status = "Running", occurredAtUtc }, cancellationToken);

    public Task NotifyWorkflowCompletedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowCompleted", new { executionId, status = "Completed", terminationReason = (string?)null, occurredAtUtc }, cancellationToken);

    public Task NotifyWorkflowFailedAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowFailed", new { executionId, status = "Failed", terminationReason, occurredAtUtc }, cancellationToken);

    public Task NotifyWorkflowCancelledAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "workflowCancelled", new { executionId, status = "Cancelled", terminationReason, occurredAtUtc }, cancellationToken);

    public Task NotifyUsageUpdatedAsync(string userId, Guid executionId, int? inputTokenCount, int? outputTokenCount, decimal? estimatedCost, CancellationToken cancellationToken = default) =>
        Send(userId, "usageUpdated", new { executionId, inputTokenCount, outputTokenCount, estimatedCost }, cancellationToken);

    private Task Send(string userId, string method, object payload, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(WorkflowExecutionHub.UserGroup(userId)).SendAsync(method, payload, cancellationToken);
}
