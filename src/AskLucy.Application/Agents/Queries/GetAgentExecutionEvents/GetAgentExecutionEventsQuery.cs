using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionEvents;

/// <summary>Reconciliation fallback for a client that missed a live <c>AgentExecutionHub</c> push (contracts/agent-execution-events.md) — every event strictly after <paramref name="Since"/>, oldest first.</summary>
public sealed record GetAgentExecutionEventsQuery(Guid ExecutionId, DateTime? Since) : IRequest<IReadOnlyList<AgentExecutionEventDto>>;
