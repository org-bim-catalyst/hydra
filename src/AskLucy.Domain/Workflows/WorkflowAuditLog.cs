using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowAuditAction
{
    WorkflowCreated,
    WorkflowModified,
    WorkflowPublished,
    ExecutionStarted,
    ExecutionCompleted,
    ExecutionFailed,
    ExecutionCancelled,
    ApprovalDecided,
    PermissionDenied,
    CrossUserAccessAttempted,
}

/// <summary>Tamper-resistant security record (FR-052, data-model.md) — append-only, deliberately not hard-FK'd to <see cref="Workflow"/>/<see cref="WorkflowExecution"/> so an entry for a later-purged workflow is retained (mirrors <c>AgentAuditLog</c>).</summary>
public sealed class WorkflowAuditLog : BaseEntity
{
    public Guid? WorkflowId { get; private set; }

    public Guid? WorkflowExecutionId { get; private set; }

    public string ActorUserId { get; private set; } = string.Empty;

    public WorkflowAuditAction Action { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public DateTime OccurredAtUtc { get; private set; }

    private WorkflowAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowAuditLog Create(Guid? workflowId, Guid? workflowExecutionId, string actorUserId, WorkflowAuditAction action, string detailsJson) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowId = workflowId,
        WorkflowExecutionId = workflowExecutionId,
        ActorUserId = actorUserId,
        Action = action,
        DetailsJson = detailsJson,
        OccurredAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = "system",
    };
}
