using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>FR-038, mirrors <c>AgentExecutionStepStatus</c>. Maps to FR-038's outcome vocabulary as: Succeed→<see cref="Completed"/>, Fail→<see cref="Failed"/>, Skip→<see cref="Skipped"/>, Cancel→<see cref="Cancelled"/>; Wait→<see cref="WaitingForApproval"/> (approval-specific; a node awaiting a plain retry backoff stays <see cref="Running"/>); Retry is not a distinct status — see <see cref="WorkflowExecutionNode.RetryCount"/>.</summary>
public enum WorkflowExecutionNodeStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Cancelled,
    WaitingForApproval,
}

/// <summary>The record of one node's execution within a <see cref="WorkflowExecution"/> (FR-045/FR-051, data-model.md) — child of <see cref="WorkflowExecution"/>, reachable only via its <c>Nodes</c> navigation. One row per <c>(WorkflowExecutionId, WorkflowNodeId)</c> pair — a resumed execution reuses its existing row rather than inserting a duplicate.</summary>
public sealed class WorkflowExecutionNode : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public Guid WorkflowNodeId { get; private set; }

    public WorkflowExecutionNodeStatus Status { get; private set; } = WorkflowExecutionNodeStatus.Pending;

    public string? InputJson { get; private set; }

    public string? OutputJson { get; private set; }

    public int RetryCount { get; private set; }

    /// <summary>Set when the owning <see cref="WorkflowNode.IdempotencyKeyExpression"/> is configured; checked before a mutating retry (research.md Decision 13).</summary>
    public string? ResolvedIdempotencyKey { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? SkippedReason { get; private set; }

    private WorkflowExecutionNode()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowExecutionNode Create(Guid workflowExecutionId, Guid workflowNodeId) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        WorkflowNodeId = workflowNodeId,
        Status = WorkflowExecutionNodeStatus.Pending,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void Start(string? inputJson)
    {
        Status = WorkflowExecutionNodeStatus.Running;
        InputJson = inputJson;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void Complete(string? outputJson, string? resolvedIdempotencyKey)
    {
        Status = WorkflowExecutionNodeStatus.Completed;
        OutputJson = outputJson;
        ResolvedIdempotencyKey = resolvedIdempotencyKey;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail()
    {
        Status = WorkflowExecutionNodeStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void IncrementRetryCount() => RetryCount++;

    public void Skip(string reason)
    {
        Status = WorkflowExecutionNodeStatus.Skipped;
        SkippedReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = WorkflowExecutionNodeStatus.Cancelled;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void WaitForApproval() => Status = WorkflowExecutionNodeStatus.WaitingForApproval;

    /// <summary>spec.md User Story 7 — a user-initiated manual retry of a failed node (<c>RetryWorkflowExecutionNodeCommand</c>). Resets to <see cref="Pending"/> so <c>WorkflowExecutionOrchestrator.RunAsync</c>'s resume logic re-enters at this same row (reusing its <see cref="RetryCount"/>/<see cref="ResolvedIdempotencyKey"/> history) rather than restarting the whole graph.</summary>
    public void ResetForRetry()
    {
        if (Status != WorkflowExecutionNodeStatus.Failed)
        {
            throw new DomainRuleViolationException("Only a failed node can be reset for retry.");
        }

        Status = WorkflowExecutionNodeStatus.Pending;
        CompletedAtUtc = null;
    }
}
