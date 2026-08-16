using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowErrorCategory
{
    NodeExecutionFailure,
    BudgetExceeded,
    Timeout,
    ValidationFailure,
    PermissionDenied,
    ProviderFailure,
}

/// <summary>A structured failure record (FR-051, data-model.md) — never a raw provider stack trace, always an actionable, user-safe message (constitution §2.VIII No Silent Failures).</summary>
public sealed class WorkflowError : BaseEntity
{
    public Guid WorkflowExecutionId { get; private set; }

    public Guid? WorkflowExecutionNodeId { get; private set; }

    public WorkflowErrorCategory Category { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public int RetryCount { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    private WorkflowError()
    {
        // Required by EF Core materialization.
    }

    internal static WorkflowError Create(Guid workflowExecutionId, Guid? workflowExecutionNodeId, WorkflowErrorCategory category, string message, int retryCount) => new()
    {
        Id = Guid.CreateVersion7(),
        WorkflowExecutionId = workflowExecutionId,
        WorkflowExecutionNodeId = workflowExecutionNodeId,
        Category = category,
        Message = message,
        RetryCount = retryCount,
        OccurredAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
    };
}
