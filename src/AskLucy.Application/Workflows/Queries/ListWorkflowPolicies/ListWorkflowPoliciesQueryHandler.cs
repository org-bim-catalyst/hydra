using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowPolicies;

public sealed class ListWorkflowPoliciesQueryHandler(IWorkflowPolicyRepository policyRepository)
    : IRequestHandler<ListWorkflowPoliciesQuery, IReadOnlyList<WorkflowPolicyDto>>
{
    public async Task<IReadOnlyList<WorkflowPolicyDto>> Handle(ListWorkflowPoliciesQuery request, CancellationToken cancellationToken) =>
        (await policyRepository.ListAllAsync(cancellationToken)).Select(WorkflowPolicyDto.Create).ToList();
}
