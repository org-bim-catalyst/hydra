using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Queries.GetWorkflowVersion;

public sealed class GetWorkflowVersionQueryHandler(IWorkflowRepository workflowRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetWorkflowVersionQuery, WorkflowVersionDto>
{
    public async Task<WorkflowVersionDto> Handle(GetWorkflowVersionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.WorkflowId, userId, cancellationToken), userId);

        var version = await workflowRepository.GetVersionAsync(request.WorkflowId, request.VersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow version not found.");

        return WorkflowVersionDto.Create(version);
    }
}
