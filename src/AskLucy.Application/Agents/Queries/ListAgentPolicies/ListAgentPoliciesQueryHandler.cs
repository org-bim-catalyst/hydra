using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentPolicies;

public sealed class ListAgentPoliciesQueryHandler(IAgentPolicyRepository policyRepository)
    : IRequestHandler<ListAgentPoliciesQuery, IReadOnlyList<AgentPolicyDto>>
{
    public async Task<IReadOnlyList<AgentPolicyDto>> Handle(ListAgentPoliciesQuery request, CancellationToken cancellationToken) =>
        (await policyRepository.ListAllAsync(cancellationToken)).Select(AgentPolicyDto.Create).ToList();
}
