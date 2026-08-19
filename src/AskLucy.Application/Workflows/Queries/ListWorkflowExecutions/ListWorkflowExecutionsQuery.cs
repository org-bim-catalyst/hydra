using AskLucy.Application.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowExecutions;

public sealed record ListWorkflowExecutionsQuery(
    Guid? WorkflowId, WorkflowExecutionStatus? Status, WorkflowExecutionTriggerType? TriggerType, string? Cursor, int PageSize)
    : IRequest<PagedResult<WorkflowExecutionSummaryDto>>;
