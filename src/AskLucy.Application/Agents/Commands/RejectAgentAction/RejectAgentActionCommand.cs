using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.RejectAgentAction;

/// <summary>Interactive rejection of a paused High/Critical-risk tool call (spec.md FR-025/FR-027/FR-028) — the tool call never executes; the execution ends with an explanation (spec.md User Story 3, acceptance scenario 3).</summary>
public sealed record RejectAgentActionCommand(Guid AgentExecutionId, Guid ApprovalId, string? Reason) : IRequest<AgentApprovalDto>;
