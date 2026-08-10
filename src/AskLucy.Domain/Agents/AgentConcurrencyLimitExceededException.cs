namespace AskLucy.Domain.Agents;

/// <summary>
/// Thrown by <see cref="Application.Agents.Commands.StartAgentExecution.StartAgentExecutionCommandHandler"/>
/// when the caller is already at their concurrent-execution cap (spec.md FR-042/FR-043) — mapped
/// to <c>429 Too Many Requests</c>, distinct from <see cref="Common.DomainRuleViolationException"/>'s
/// generic 400 mapping because this is a rate/capacity limit, not an invalid request.
/// </summary>
public sealed class AgentConcurrencyLimitExceededException(int maxConcurrentExecutions)
    : Exception($"You already have {maxConcurrentExecutions} agent execution(s) in progress, which is your current limit. Wait for one to finish, or cancel one, before starting another.")
{
    public int MaxConcurrentExecutions { get; } = maxConcurrentExecutions;
}
