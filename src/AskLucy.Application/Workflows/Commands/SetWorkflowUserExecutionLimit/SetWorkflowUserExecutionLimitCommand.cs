using MediatR;

namespace AskLucy.Application.Workflows.Commands.SetWorkflowUserExecutionLimit;

public sealed record WorkflowUserExecutionLimitDto(string UserId, int MaxConcurrentExecutions);

/// <summary>Administrator-managed per-user override of the concurrent-execution cap (spec.md FR-069/FR-070, research.md Decision 11) — Administrator/Super User only, enforced by the controller's <c>AdministratorOrSuperUser</c> authorization policy. Mirrors <c>SetAgentUserExecutionLimitCommand</c> exactly.</summary>
public sealed record SetWorkflowUserExecutionLimitCommand(string UserId, int MaxConcurrentExecutions) : IRequest<WorkflowUserExecutionLimitDto>;
