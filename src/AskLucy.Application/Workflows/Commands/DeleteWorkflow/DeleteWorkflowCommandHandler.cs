using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.DeleteWorkflow;

public sealed class DeleteWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteWorkflowCommand>
{
    public async Task Handle(DeleteWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        workflow.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
