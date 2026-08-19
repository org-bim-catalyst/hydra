using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowPolicies;

public sealed record ListWorkflowPoliciesQuery : IRequest<IReadOnlyList<WorkflowPolicyDto>>;
