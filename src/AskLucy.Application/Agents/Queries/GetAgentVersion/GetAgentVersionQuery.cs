using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentVersion;

public sealed record GetAgentVersionQuery(Guid AgentId, int VersionNumber) : IRequest<AgentVersionDto>;
