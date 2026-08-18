using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentPolicies;

public sealed record ListAgentPoliciesQuery : IRequest<IReadOnlyList<AgentPolicyDto>>;
