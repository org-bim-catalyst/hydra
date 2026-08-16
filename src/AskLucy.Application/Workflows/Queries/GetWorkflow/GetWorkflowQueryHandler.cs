using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflow;

public sealed class GetWorkflowQueryHandler(IWorkflowRepository workflowRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowQuery, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(GetWorkflowQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        return WorkflowDetailDto.Create(workflow);
    }
}
