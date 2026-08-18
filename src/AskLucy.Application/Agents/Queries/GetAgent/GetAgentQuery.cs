using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgent;

public sealed record GetAgentQuery(Guid Id) : IRequest<AgentDetailDto>;
