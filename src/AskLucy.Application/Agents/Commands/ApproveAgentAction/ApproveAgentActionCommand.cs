using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.ApproveAgentAction;

/// <summary>Interactive approval of a paused High/Critical-risk tool call (spec.md FR-025/FR-027/FR-028) — re-enqueues the execution to resume from where it paused.</summary>
public sealed record ApproveAgentActionCommand(Guid AgentExecutionId, Guid ApprovalId) : IRequest<AgentApprovalDto>;
