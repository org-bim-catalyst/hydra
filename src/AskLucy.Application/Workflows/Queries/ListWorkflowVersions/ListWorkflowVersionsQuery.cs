using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowVersions;

public sealed record ListWorkflowVersionsQuery(Guid WorkflowId) : IRequest<IReadOnlyList<WorkflowVersionDto>>;
