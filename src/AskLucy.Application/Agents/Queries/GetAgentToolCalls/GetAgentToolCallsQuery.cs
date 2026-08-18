using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentToolCalls;

public sealed record GetAgentToolCallsQuery(Guid ExecutionId) : IRequest<IReadOnlyList<AgentToolCallDto>>;
