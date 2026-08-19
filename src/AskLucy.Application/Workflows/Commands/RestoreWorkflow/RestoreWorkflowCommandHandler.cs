using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.RestoreWorkflow;

public sealed class RestoreWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RestoreWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(RestoreWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        workflow.Restore(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
