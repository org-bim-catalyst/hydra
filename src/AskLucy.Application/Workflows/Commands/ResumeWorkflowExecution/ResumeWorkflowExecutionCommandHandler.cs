using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.ResumeWorkflowExecution;

public sealed class ResumeWorkflowExecutionCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowExecutionRunner runner, IWorkflowExecutionNotifier notifier,
    IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ResumeWorkflowExecutionCommand>
{
    public async Task Handle(ResumeWorkflowExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        if (execution.Status != WorkflowExecutionStatus.Paused)
        {
            throw new DomainRuleViolationException("Only a paused execution can be resumed this way — a WaitingForApproval execution resumes via the approval decision instead.");
        }

        execution.Resume();
        execution.RecordEvent(WorkflowExecutionEventType.WorkflowResumed, workflowNodeId: null, "Running", null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyWorkflowResumedAsync(userId, execution.Id, DateTime.UtcNow, cancellationToken);
        await runner.EnqueueAsync(execution.Id, cancellationToken);
    }
}
