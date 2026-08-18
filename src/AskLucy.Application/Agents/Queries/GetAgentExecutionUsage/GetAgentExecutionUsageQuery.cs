using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionUsage;

public sealed record GetAgentExecutionUsageQuery(Guid ExecutionId) : IRequest<AgentExecutionUsageDto>;
