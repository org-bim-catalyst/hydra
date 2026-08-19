using AskLucy.Application.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowExecutionUsage;

public sealed record GetWorkflowExecutionUsageQuery(Guid ExecutionId) : IRequest<WorkflowExecutionUsageDto>;
