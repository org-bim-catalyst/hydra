using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowExecutions;

public sealed class ListWorkflowExecutionsQueryHandler(IWorkflowExecutionRepository executionRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListWorkflowExecutionsQuery, PagedResult<WorkflowExecutionSummaryDto>>
{
    public async Task<PagedResult<WorkflowExecutionSummaryDto>> Handle(ListWorkflowExecutionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var (items, nextCursor) = await executionRepository.ListByUserAsync(
            userId, request.WorkflowId, request.Status, request.TriggerType, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<WorkflowExecutionSummaryDto>(items.Select(WorkflowExecutionSummaryDto.Create).ToList(), nextCursor);
    }
}
