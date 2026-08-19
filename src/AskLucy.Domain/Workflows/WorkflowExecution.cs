using AskLucy.Domain.Common;

namespace AskLucy.Domain.Workflows;

public enum WorkflowExecutionStatus
{
    Queued,
    Running,
    Paused,
    WaitingForApproval,
    Completed,
    Failed,
    Cancelled,
    TimedOut,
}

public enum WorkflowExecutionTriggerType
{
    Manual,
    EventDriven,
    Test,
}

/// <summary>
/// One run of a specific <see cref="WorkflowVersion"/> (FR-044/FR-045, data-model.md). Aggregate
/// root for the execution bounded context — owns its <see cref="WorkflowExecutionNode"/>/
/// <see cref="WorkflowExecutionEvent"/>/<see cref="WorkflowApproval"/>/<see cref="WorkflowError"/>
/// history plus <see cref="Usage"/>/<see cref="Cost"/>. Never hard-deleted (FR-052 audit trail);
/// node/event/error rows are append-only once written even though <see cref="Status"/> itself
/// mutates through the execution's lifecycle.
/// </summary>
public sealed class WorkflowExecution : BaseEntity
{
    private readonly List<WorkflowExecutionNode> _nodes = [];
    private readonly List<WorkflowExecutionEvent> _events = [];
    private readonly List<WorkflowApproval> _approvals = [];
    private readonly List<WorkflowError> _errors = [];

    public Guid WorkflowId { get; private set; }

    public Guid WorkflowVersionId { get; private set; }

    public string RunByUserId { get; private set; } = string.Empty;

    public WorkflowExecutionStatus Status { get; private set; }

    public WorkflowExecutionTriggerType TriggerType { get; private set; }

    /// <summary>Set only for <see cref="WorkflowExecutionTriggerType.EventDriven"/> — the source event type + entity id (FR-063).</summary>
    public string? TriggeringEventReferenceJson { get; private set; }

    public string InputsJson { get; private set; } = "{}";

    public string VariablesJson { get; private set; } = "{}";

    public string? FinalOutputJson { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? TerminationReason { get; private set; }

    public WorkflowExecutionUsage? Usage { get; private set; }

    public WorkflowExecutionCost? Cost { get; private set; }

    public IReadOnlyCollection<WorkflowExecutionNode> Nodes => _nodes;

    public IReadOnlyCollection<WorkflowExecutionEvent> Events => _events;

    public IReadOnlyCollection<WorkflowApproval> Approvals => _approvals;

    public IReadOnlyCollection<WorkflowError> Errors => _errors;

    private WorkflowExecution()
    {
        // Required by EF Core materialization.
    }

    public static WorkflowExecution Create(
        Guid workflowId, Guid workflowVersionId, string runByUserId, WorkflowExecutionTriggerType triggerType,
        string? triggeringEventReferenceJson, string inputsJson, string actor)
    {
        if (string.IsNullOrWhiteSpace(runByUserId))
        {
            throw new DomainRuleViolationException("An execution must run as a specific user.");
        }

        return new WorkflowExecution
        {
            Id = Guid.CreateVersion7(),
            WorkflowId = workflowId,
            WorkflowVersionId = workflowVersionId,
            RunByUserId = runByUserId,
            Status = WorkflowExecutionStatus.Queued,
            TriggerType = triggerType,
            TriggeringEventReferenceJson = triggeringEventReferenceJson,
            InputsJson = inputsJson,
            VariablesJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Start()
    {
        Status = WorkflowExecutionStatus.Running;
        StartedAtUtc ??= DateTime.UtcNow;
    }

    public void SetVariables(string variablesJson) => VariablesJson = variablesJson;

    public WorkflowExecutionNode AddNode(Guid workflowNodeId)
    {
        var node = WorkflowExecutionNode.Create(Id, workflowNodeId);
        _nodes.Add(node);
        return node;
    }

    public WorkflowExecutionEvent RecordEvent(WorkflowExecutionEventType eventType, Guid? workflowNodeId, string status, string? safeMetadataJson)
    {
        var evt = WorkflowExecutionEvent.Create(Id, eventType, workflowNodeId, status, safeMetadataJson);
        _events.Add(evt);
        return evt;
    }

    public WorkflowApproval RequestApproval(Guid workflowExecutionNodeId, string intendedActionDescription, string? parametersJson, int? timeoutSeconds)
    {
        var approval = WorkflowApproval.CreatePending(Id, workflowExecutionNodeId, intendedActionDescription, parametersJson, timeoutSeconds);
        _approvals.Add(approval);
        Status = WorkflowExecutionStatus.WaitingForApproval;
        return approval;
    }

    public WorkflowError RecordError(WorkflowErrorCategory category, string message, Guid? workflowExecutionNodeId, int retryCount)
    {
        var error = WorkflowError.Create(Id, workflowExecutionNodeId, category, message, retryCount);
        _errors.Add(error);
        return error;
    }

    public void Pause()
    {
        if (Status is WorkflowExecutionStatus.Running)
        {
            Status = WorkflowExecutionStatus.Paused;
        }
    }

    public void Resume()
    {
        if (Status is WorkflowExecutionStatus.Paused or WorkflowExecutionStatus.WaitingForApproval)
        {
            Status = WorkflowExecutionStatus.Running;
        }
    }

    public void Cancel(string reason)
    {
        Status = WorkflowExecutionStatus.Cancelled;
        TerminationReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Complete(string? finalOutputJson)
    {
        Status = WorkflowExecutionStatus.Completed;
        FinalOutputJson = finalOutputJson;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void Fail(string reason)
    {
        Status = WorkflowExecutionStatus.Failed;
        TerminationReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>spec.md User Story 7 — reopens a `Failed` execution for a manual node retry (<c>RetryWorkflowExecutionNodeCommand</c>), paired with the retried <see cref="WorkflowExecutionNode.ResetForRetry"/>.</summary>
    public void ReopenForRetry()
    {
        if (Status != WorkflowExecutionStatus.Failed)
        {
            throw new DomainRuleViolationException("Only a failed execution can be reopened for a manual node retry.");
        }

        Status = WorkflowExecutionStatus.Running;
        TerminationReason = null;
        CompletedAtUtc = null;
    }

    public void TimeOut(string reason)
    {
        Status = WorkflowExecutionStatus.TimedOut;
        TerminationReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }

    public void SetUsage(WorkflowExecutionUsage usage) => Usage = usage;

    public void SetCost(WorkflowExecutionCost cost) => Cost = cost;
}
