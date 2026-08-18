using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentExecutionSteps;

public sealed record GetAgentExecutionStepsQuery(Guid ExecutionId) : IRequest<IReadOnlyList<AgentExecutionStepDto>>;
