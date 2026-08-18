using MediatR;

namespace AskLucy.Application.Agents.Commands.SetAgentUserExecutionLimit;

public sealed record AgentUserExecutionLimitDto(string UserId, int MaxConcurrentExecutions);

/// <summary>Administrator-managed per-user override of the concurrent-execution cap (spec.md FR-042/FR-043, research.md Decision 2) — Administrator/Super User only, enforced by the controller's <c>AdministratorOrSuperUser</c> authorization policy.</summary>
public sealed record SetAgentUserExecutionLimitCommand(string UserId, int MaxConcurrentExecutions) : IRequest<AgentUserExecutionLimitDto>;
