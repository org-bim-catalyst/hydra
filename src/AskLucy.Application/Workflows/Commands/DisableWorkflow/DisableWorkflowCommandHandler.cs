using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.DisableWorkflow;

public sealed class DisableWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DisableWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(DisableWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        workflow.Disable(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
