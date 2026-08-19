using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflows;

public sealed class ListWorkflowsQueryHandler(IWorkflowRepository workflowRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListWorkflowsQuery, PagedResult<WorkflowListItemDto>>
{
    public async Task<PagedResult<WorkflowListItemDto>> Handle(ListWorkflowsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await workflowRepository.ListByOwnerAsync(
            userId, request.Status, request.WorkflowType, request.Search, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<WorkflowListItemDto>(items.Select(WorkflowListItemDto.Create).ToList(), nextCursor);
    }
}
