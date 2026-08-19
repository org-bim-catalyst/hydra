using AskLucy.Application.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionNodes;

public sealed record GetWorkflowExecutionNodesQuery(Guid ExecutionId) : IRequest<IReadOnlyList<WorkflowExecutionNodeDto>>;
