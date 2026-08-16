namespace AskLucy.Domain.Workflows;

/// <summary>
/// Thrown by <see cref="Application.Workflows.Commands.StartWorkflowExecution.StartWorkflowExecutionCommandHandler"/>
/// when the caller is already at their concurrent-execution cap (spec.md FR-069/FR-070) — mapped
/// to <c>429 Too Many Requests</c>, distinct from <see cref="Common.DomainRuleViolationException"/>'s
/// generic 400 mapping because this is a rate/capacity limit, not an invalid request. Also thrown
/// from <see cref="Application.Workflows.EventTriggers.WorkflowEventTriggerHandler"/> when an
/// event-triggered dispatch would exceed the cap (FR-070, Acceptance Scenario 9.4).
/// </summary>
public sealed class WorkflowConcurrencyLimitExceededException(int maxConcurrentExecutions)
    : Exception($"You already have {maxConcurrentExecutions} workflow execution(s) in progress, which is your current limit. Wait for one to finish, or cancel one, before starting another.")
{
    public int MaxConcurrentExecutions { get; } = maxConcurrentExecutions;
}
