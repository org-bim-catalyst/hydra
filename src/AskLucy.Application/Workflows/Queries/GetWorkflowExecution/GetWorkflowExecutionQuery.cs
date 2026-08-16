using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecution;

public sealed record GetWorkflowExecutionQuery(Guid Id) : IRequest<WorkflowExecutionDetailDto>;
