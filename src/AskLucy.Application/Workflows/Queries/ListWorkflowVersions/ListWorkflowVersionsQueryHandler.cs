using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.ListWorkflowVersions;

public sealed class ListWorkflowVersionsQueryHandler(IWorkflowRepository workflowRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListWorkflowVersionsQuery, IReadOnlyList<WorkflowVersionDto>>
{
    public async Task<IReadOnlyList<WorkflowVersionDto>> Handle(ListWorkflowVersionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.WorkflowId, userId, cancellationToken), userId);

        var versions = await workflowRepository.ListVersionsAsync(workflow.Id, cancellationToken);
        return versions.OrderByDescending(v => v.VersionNumber).Select(WorkflowVersionDto.Create).ToList();
    }
}
