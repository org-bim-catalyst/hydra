using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents;

public sealed record AgentExecutionStepDto(
    Guid Id, int StepIndex, string Description, string StepType, string Status,
    Guid? DependsOnStepId, string? ToolName, string? OutputJson, DateTime? StartedAtUtc, DateTime? CompletedAtUtc)
{
    public static AgentExecutionStepDto Create(AgentExecutionStep step) => new(
        step.Id, step.StepIndex, step.Description, step.StepType.ToString(), step.Status.ToString(),
        step.DependsOnStepId, step.ToolName, step.OutputJson, step.StartedAtUtc, step.CompletedAtUtc);
}

public sealed record AgentApprovalDto(
    Guid Id, Guid? AgentToolCallId, string IntendedActionDescription, string IntendedParametersJson,
    string Decision, string? DecidedByUserId, bool WasPolicyBased, DateTime? DecidedAtUtc)
{
    public static AgentApprovalDto Create(AgentApproval approval) => new(
        approval.Id, approval.AgentToolCallId, approval.IntendedActionDescription, approval.IntendedParametersJson,
        approval.Decision.ToString(), approval.DecidedByUserId, approval.WasPolicyBased, approval.DecidedAtUtc);
}

public sealed record AgentExecutionErrorDto(Guid Id, string Category, string Message, int RetryCount, DateTime OccurredAtUtc)
{
    public static AgentExecutionErrorDto Create(AgentExecutionError error) => new(
        error.Id, error.Category.ToString(), error.Message, error.RetryCount, error.OccurredAtUtc);
}

/// <summary>Reconciliation payload for a client that missed a live push (contracts/agent-execution-events.md) — mirrors the exact shape already sent over <c>AgentExecutionHub</c>.</summary>
public sealed record AgentExecutionEventDto(Guid Id, Guid? StepId, string EventType, string Status, string? SafeMetadataJson, DateTime OccurredAtUtc)
{
    public static AgentExecutionEventDto Create(AgentExecutionEvent evt) => new(
        evt.Id, evt.StepId, evt.EventType.ToString(), evt.Status, evt.SafeMetadataJson, evt.OccurredAtUtc);
}

/// <summary>User Story 5 execution history — every field spec.md's Execution History section requires for a specific tool invocation.</summary>
public sealed record AgentToolCallDto(
    Guid Id, Guid AgentExecutionStepId, string ToolName, string RiskLevel, string RequiredPermissionsJson,
    string ValidatedInputJson, string? ValidatedOutputJson, string? FailureReason, bool WasApprovalRequired,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc)
{
    public static AgentToolCallDto Create(AgentToolCall toolCall) => new(
        toolCall.Id, toolCall.AgentExecutionStepId, toolCall.ToolName, toolCall.RiskLevel.ToString(), toolCall.RequiredPermissionsJson,
        toolCall.ValidatedInputJson, toolCall.ValidatedOutputJson, toolCall.FailureReason, toolCall.WasApprovalRequired,
        toolCall.StartedAtUtc, toolCall.CompletedAtUtc);
}

/// <summary>User Story 5 execution history — usage and cost together (spec.md FR-036).</summary>
public sealed record AgentExecutionUsageDto(int? InputTokenCount, int? OutputTokenCount, int? ReasoningTokenCount, int ToolCallCount, int StepCount, decimal? EstimatedCost, string? CostCurrency)
{
    public static AgentExecutionUsageDto Create(AgentExecutionUsage? usage, AgentExecutionCost? cost) => new(
        usage?.InputTokenCount, usage?.OutputTokenCount, usage?.ReasoningTokenCount, usage?.ToolCallCount ?? 0, usage?.StepCount ?? 0,
        cost?.EstimatedCost, cost?.Currency);
}

/// <summary>Full execution history assembly (spec.md FR-036/User Story 5) — every field spec.md's Execution History section requires in one response.</summary>
public sealed record AgentExecutionDetailDto(
    Guid Id,
    Guid AgentId,
    Guid AgentVersionId,
    int AgentVersionNumber,
    string Objective,
    string Status,
    bool IsTestExecution,
    string ConversationIntegrationMode,
    Guid? UserChatId,
    string? FinalOutputText,
    string? FinalOutputJson,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? TerminationReason,
    IReadOnlyList<AgentExecutionStepDto> Steps,
    IReadOnlyList<AgentApprovalDto> Approvals,
    IReadOnlyList<AgentExecutionErrorDto> Errors,
    int? InputTokenCount,
    int? OutputTokenCount,
    decimal? EstimatedCost,
    DateTime CreatedAtUtc)
{
    public static AgentExecutionDetailDto Create(AgentExecution execution, int agentVersionNumber) => new(
        execution.Id, execution.AgentId, execution.AgentVersionId, agentVersionNumber, execution.Objective,
        execution.Status.ToString(), execution.IsTestExecution, execution.ConversationIntegrationMode.ToString(),
        execution.UserChatId, execution.FinalOutputText, execution.FinalOutputJson, execution.StartedAtUtc,
        execution.CompletedAtUtc, execution.TerminationReason,
        execution.Steps.OrderBy(s => s.StepIndex).Select(AgentExecutionStepDto.Create).ToList(),
        execution.Approvals.Select(AgentApprovalDto.Create).ToList(),
        execution.Errors.Select(AgentExecutionErrorDto.Create).ToList(),
        execution.Usage?.InputTokenCount, execution.Usage?.OutputTokenCount, execution.Cost?.EstimatedCost,
        execution.CreatedAtUtc);
}

public sealed record AgentExecutionSummaryDto(Guid Id, Guid AgentId, string Status, bool IsTestExecution, DateTime CreatedAtUtc)
{
    /// <summary>Only ever set by <c>StartAgentExecutionCommandHandler</c>, which observes the Hangfire job id
    /// <see cref="Abstractions.IAgentExecutionRunner.EnqueueAsync"/> returns — non-positional so this
    /// factory's existing callers are unaffected.</summary>
    public string? HangfireJobId { get; init; }

    public static AgentExecutionSummaryDto Create(AgentExecution execution) => new(
        execution.Id, execution.AgentId, execution.Status.ToString(), execution.IsTestExecution, execution.CreatedAtUtc);
}

