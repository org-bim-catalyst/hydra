using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.DuplicateWorkflow;

public sealed class DuplicateWorkflowCommandHandler(IWorkflowRepository workflowRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DuplicateWorkflowCommand, WorkflowDetailDto>
{
    public async Task<WorkflowDetailDto> Handle(DuplicateWorkflowCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var workflow = WorkflowOwnershipGuard.EnsureOwnedBy(await workflowRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        var copy = workflow.Duplicate(userId);
        workflowRepository.Add(copy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WorkflowDetailDto.Create(copy);
    }
}
