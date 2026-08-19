using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.EnableWorkflow;

public sealed class EnableWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<EnableWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(EnableWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        workflow.Enable(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(workflow);
    }
}
