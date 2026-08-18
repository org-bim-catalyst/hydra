using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentVersions;

public sealed record ListAgentVersionsQuery(Guid AgentId) : IRequest<IReadOnlyList<AgentVersionDto>>;
