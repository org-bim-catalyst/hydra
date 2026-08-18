using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Agents;

/// <summary>Pure push over <see cref="AgentExecutionHub"/> (contracts/agent-execution-events.md) — persistence of the corresponding <c>AgentExecutionEvent</c> row happens separately in the orchestrator via <c>AgentExecution.RecordEvent</c>, mirroring <c>RetrievalIndexingNotifier</c>'s push-only shape.</summary>
public sealed class AgentExecutionNotifier(IHubContext<AgentExecutionHub> hubContext) : IAgentExecutionNotifier
{
    public Task NotifyExecutionStartedAsync(string userId, Guid executionId, Guid agentId, int agentVersionNumber, string objective, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "executionStarted", new { executionId, agentId, agentVersionNumber, objective, occurredAtUtc }, cancellationToken);

    public Task NotifyPlanCreatedAsync(string userId, Guid executionId, int stepCount, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "planCreated", new { executionId, stepCount, occurredAtUtc }, cancellationToken);

    public Task NotifyStepStartedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "stepStarted", new { executionId, stepId, stepIndex, description, status = "Running", occurredAtUtc }, cancellationToken);

    public Task NotifyStepCompletedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "stepCompleted", new { executionId, stepId, stepIndex, description, status = "Completed", occurredAtUtc }, cancellationToken);

    public Task NotifyStepFailedAsync(string userId, Guid executionId, Guid stepId, int stepIndex, string description, string errorMessage, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "stepFailed", new { executionId, stepId, stepIndex, description, status = "Failed", occurredAtUtc, errorMessage }, cancellationToken);

    public Task NotifyToolCallStartedAsync(string userId, Guid executionId, Guid stepId, string toolName, AgentToolRiskLevel riskLevel, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "toolCallStarted", new { executionId, stepId, toolName, riskLevel = riskLevel.ToString(), status = "Running", occurredAtUtc }, cancellationToken);

    public Task NotifyToolCallCompletedAsync(string userId, Guid executionId, Guid stepId, string toolName, AgentToolRiskLevel riskLevel, bool succeeded, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "toolCallCompleted", new { executionId, stepId, toolName, riskLevel = riskLevel.ToString(), status = succeeded ? "Completed" : "Failed", occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalRequestedAsync(string userId, Guid executionId, Guid approvalId, string intendedActionDescription, AgentToolRiskLevel riskLevel, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalRequested", new { executionId, approvalId, intendedActionDescription, riskLevel = riskLevel.ToString(), occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalGrantedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, bool wasPolicyBased, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalGranted", new { executionId, approvalId, decidedByUserId, wasPolicyBased, occurredAtUtc }, cancellationToken);

    public Task NotifyApprovalRejectedAsync(string userId, Guid executionId, Guid approvalId, string? decidedByUserId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "approvalRejected", new { executionId, approvalId, decidedByUserId, wasPolicyBased = false, occurredAtUtc }, cancellationToken);

    public Task NotifyExecutionCompletedAsync(string userId, Guid executionId, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "executionCompleted", new { executionId, status = "Completed", terminationReason = (string?)null, occurredAtUtc }, cancellationToken);

    public Task NotifyExecutionFailedAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "executionFailed", new { executionId, status = "Failed", terminationReason, occurredAtUtc }, cancellationToken);

    public Task NotifyExecutionCancelledAsync(string userId, Guid executionId, string terminationReason, DateTime occurredAtUtc, CancellationToken cancellationToken = default) =>
        Send(userId, "executionCancelled", new { executionId, status = "Cancelled", terminationReason, occurredAtUtc }, cancellationToken);

    public Task NotifyUsageUpdatedAsync(string userId, Guid executionId, int? inputTokenCount, int? outputTokenCount, decimal? estimatedCost, CancellationToken cancellationToken = default) =>
        Send(userId, "usageUpdated", new { executionId, inputTokenCount, outputTokenCount, estimatedCost }, cancellationToken);

    private Task Send(string userId, string method, object payload, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(AgentExecutionHub.UserGroup(userId)).SendAsync(method, payload, cancellationToken);
}
