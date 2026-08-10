using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecution;

public sealed record GetAgentExecutionQuery(Guid Id) : IRequest<AgentExecutionDetailDto>;
