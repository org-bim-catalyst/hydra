using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows;

public sealed record WorkflowExecutionNodeDto(
    Guid Id, Guid WorkflowNodeId, string Status, string? OutputJson, int RetryCount, string? SkippedReason,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc)
{
    public static WorkflowExecutionNodeDto Create(WorkflowExecutionNode node) => new(
        node.Id, node.WorkflowNodeId, node.Status.ToString(), node.OutputJson, node.RetryCount, node.SkippedReason,
        node.StartedAtUtc, node.CompletedAtUtc);
}

public sealed record WorkflowErrorDto(Guid Id, string Category, string Message, int RetryCount, DateTime OccurredAtUtc)
{
    public static WorkflowErrorDto Create(WorkflowError error) => new(error.Id, error.Category.ToString(), error.Message, error.RetryCount, error.OccurredAtUtc);
}

/// <summary>Reconciliation fallback shape for a client that missed a live <c>WorkflowExecutionHub</c> push (contracts/workflow-execution-events.md).</summary>
public sealed record WorkflowExecutionEventDto(Guid Id, Guid? WorkflowNodeId, string EventType, string Status, string? SafeMetadataJson, DateTime OccurredAtUtc)
{
    public static WorkflowExecutionEventDto Create(WorkflowExecutionEvent evt) => new(
        evt.Id, evt.WorkflowNodeId, evt.EventType.ToString(), evt.Status, evt.SafeMetadataJson, evt.OccurredAtUtc);
}

public sealed record WorkflowApprovalDto(
    Guid Id, Guid WorkflowExecutionNodeId, string IntendedActionDescription, string? ParametersJson,
    string Decision, bool WasPolicyBased, string? DecidedByUserId, DateTime? DecidedAtUtc)
{
    public static WorkflowApprovalDto Create(WorkflowApproval approval) => new(
        approval.Id, approval.WorkflowExecutionNodeId, approval.IntendedActionDescription, approval.ParametersJson,
        approval.Decision.ToString(), approval.WasPolicyBased, approval.DecidedByUserId, approval.DecidedAtUtc);
}

/// <summary>Usage + cost together (spec.md User Story 8) — <see cref="WorkflowExecutionUsage"/>/<see cref="WorkflowExecutionCost"/> are separate child rows that may not exist yet for an in-flight execution.</summary>
public sealed record WorkflowExecutionUsageDto(int? InputTokenCount, int? OutputTokenCount, int? ReasoningTokenCount, int ToolCallCount, decimal? EstimatedCost, string? CostCurrency)
{
    public static WorkflowExecutionUsageDto Create(WorkflowExecutionUsage? usage, WorkflowExecutionCost? cost) => new(
        usage?.InputTokenCount, usage?.OutputTokenCount, usage?.ReasoningTokenCount, usage?.ToolCallCount ?? 0,
        cost?.EstimatedCost, cost?.CurrencyCode);
}

public sealed record WorkflowExecutionSummaryDto(Guid Id, Guid WorkflowId, string Status, string TriggerType, DateTime CreatedAtUtc)
{
    public static WorkflowExecutionSummaryDto Create(WorkflowExecution execution) => new(
        execution.Id, execution.WorkflowId, execution.Status.ToString(), execution.TriggerType.ToString(), execution.CreatedAtUtc);
}

/// <summary>Full execution history assembly (spec.md FR-051/User Story 8) — every field spec.md's Execution History section requires in one response.</summary>
public sealed record WorkflowExecutionDetailDto(
    Guid Id,
    Guid WorkflowId,
    Guid WorkflowVersionId,
    string Status,
    string TriggerType,
    string InputsJson,
    string? FinalOutputJson,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? TerminationReason,
    IReadOnlyList<WorkflowExecutionNodeDto> Nodes,
    IReadOnlyList<WorkflowApprovalDto> Approvals,
    IReadOnlyList<WorkflowErrorDto> Errors,
    int? InputTokenCount,
    int? OutputTokenCount,
    decimal? EstimatedCost,
    DateTime CreatedAtUtc)
{
    public static WorkflowExecutionDetailDto Create(WorkflowExecution execution) => new(
        execution.Id, execution.WorkflowId, execution.WorkflowVersionId, execution.Status.ToString(), execution.TriggerType.ToString(),
        execution.InputsJson, execution.FinalOutputJson, execution.StartedAtUtc, execution.CompletedAtUtc, execution.TerminationReason,
        execution.Nodes.Select(WorkflowExecutionNodeDto.Create).ToList(),
        execution.Approvals.Select(WorkflowApprovalDto.Create).ToList(),
        execution.Errors.Select(WorkflowErrorDto.Create).ToList(),
        execution.Usage?.InputTokenCount, execution.Usage?.OutputTokenCount, execution.Cost?.EstimatedCost,
        execution.CreatedAtUtc);
}
