using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

/// <summary>FR-049 — naming uses the <c>Workflow*</c>/<c>Node</c>/<c>Approval*</c> prefixes matching spec.md's literal event names, not an <c>Execution*</c> prefix.</summary>
public enum WorkflowExecutionEventType
{
    WorkflowStarted,
    NodeStarted,
    NodeCompleted,
    NodeFailed,
    NodeRetrying,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalRejected,
    WorkflowPaused,
    WorkflowResumed,
    WorkflowCompleted,
    WorkflowFailed,
    WorkflowCancelled,
    UsageUpdated,
}

/// <summary>
/// Append-only, safe-metadata-only event (FR-049/FR-053, data-model.md) — the persisted backing
/// store the real-time hub (contracts/workflow-execution-events.md) replays/pushes from. Never
/// carries chain-of-thought or raw prompt/tool content.
/// </summary>
public sealed class WorkflowExecutionEvent : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public WorkflowExecutionEventType EventType { get; private set; }

    public Guid? WorkflowNodeId { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public string? SafeMetadataJson { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private WorkflowExecutionEvent()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowExecutionEvent Create(Guid workflowExecutionId, WorkflowExecutionEventType eventType, Guid? workflowNodeId, string status, string? safeMetadataJson) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        EventType = eventType,
        WorkflowNodeId = workflowNodeId,
        Status = status,
        SafeMetadataJson = safeMetadataJson,
        OccurredAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
    };
}
