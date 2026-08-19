using AskLucy.Application.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflows;

public sealed record ListWorkflowsQuery(WorkflowStatus? Status, WorkflowType? WorkflowType, string? Search, string? Cursor, int PageSize)
    : IRequest<PagedResult<WorkflowListItemDto>>;
