using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflow;

public sealed record GetWorkflowQuery(Guid Id) : IRequest<WorkflowDetailDto>;
