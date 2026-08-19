using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;

public sealed class PauseWorkflowExecutionCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowExecutionNotifier notifier, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<PauseWorkflowExecutionCommand>
{
    public async Task Handle(PauseWorkflowExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        if (execution.Status != WorkflowExecutionStatus.Running)
        {
            return; // Not currently running — nothing to pause.
        }

        execution.Pause();
        execution.RecordEvent(WorkflowExecutionEventType.WorkflowPaused, workflowNodeId: null, "Paused", null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyWorkflowPausedAsync(userId, execution.Id, DateTime.UtcNow, cancellationToken);
    }
}
