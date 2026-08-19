using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeprecateWorkflow;

public sealed class DeprecateWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeprecateWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(DeprecateWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        workflow.Deprecate(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
