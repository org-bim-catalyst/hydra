using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowApprovalDecision
{
    Pending,
    Approve,
    Reject,
    RequestChanges,
    Cancel,
}

/// <summary>A pause-for-approval request at a Human Approval node, or at any node wrapping a High/Critical-risk capability (FR-033-FR-037, data-model.md, research.md Decision 5) — mirrors <c>AgentApproval</c>'s Pending/decision shape.</summary>
public sealed class WorkflowApproval : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public Guid WorkflowExecutionNodeId { get; private set; }

    public string IntendedActionDescription { get; private set; } = string.Empty;

    public string? ParametersJson { get; private set; }

    public WorkflowApprovalDecision Decision { get; private set; } = WorkflowApprovalDecision.Pending;

    public bool WasPolicyBased { get; private set; }

    public Guid? MatchedWorkflowPolicyId { get; private set; }

    public string? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Copied from the node's config at request time (FR-037); null = waits indefinitely.</summary>
    public int? TimeoutSeconds { get; private set; }

    private WorkflowApproval()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowApproval CreatePending(Guid workflowExecutionId, Guid workflowExecutionNodeId, string intendedActionDescription, string? parametersJson, int? timeoutSeconds) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        WorkflowExecutionNodeId = workflowExecutionNodeId,
        IntendedActionDescription = intendedActionDescription,
        ParametersJson = parametersJson,
        Decision = WorkflowApprovalDecision.Pending,
        TimeoutSeconds = timeoutSeconds,
        CreatedAtUtc = DateTime.UtcNow,
    };

    public void Approve(string userId)
    {
        Decision = WorkflowApprovalDecision.Approve;
        DecidedByUserId = userId;
        WasPolicyBased = false;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string userId)
    {
        Decision = WorkflowApprovalDecision.Reject;
        DecidedByUserId = userId;
        WasPolicyBased = false;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void RequestChanges(string userId)
    {
        Decision = WorkflowApprovalDecision.RequestChanges;
        DecidedByUserId = userId;
        WasPolicyBased = false;
        DecidedAtUtc = DateTime.UtcNow;
    }

    /// <summary>FR-037 — a configured timeout elapsed without a decision; the node's own timeout failure policy governs what happens next, but the approval itself is recorded as cancelled rather than left ambiguous.</summary>
    public void CancelByTimeout()
    {
        Decision = WorkflowApprovalDecision.Cancel;
        DecidedAtUtc = DateTime.UtcNow;
    }

    public void ApproveByPolicy(Guid policyId)
    {
        Decision = WorkflowApprovalDecision.Approve;
        WasPolicyBased = true;
        MatchedWorkflowPolicyId = policyId;
        DecidedAtUtc = DateTime.UtcNow;
    }
}
