using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.Workflows;
using MediatR;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Workflows.Commands.PauseWorkflowExecution;

public sealed class PauseWorkflowExecutionCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowRepository workflowRepository, IWorkflowExecutionNotifier notifier,
    INotificationPublisher notificationPublisher, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
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

        // T087 — the parent Workflow isn't otherwise needed by this handler, only fetched to name
        // the workflow.execution.paused notification (mirrors the orchestrator's identical lookup).
        var workflow = await workflowRepository.GetByIdAsync(execution.WorkflowId, cancellationToken);
        notificationPublisher.Publish(new NotificationRequest(
            NotificationTypeKeys.WorkflowExecutionPaused,
            new NotificationRecipient.User(userId),
            new Dictionary<string, string?> { ["workflowName"] = workflow?.Name ?? "your workflow" },
            new RelatedItem("WorkflowExecution", execution.Id.ToString(), execution.WorkflowId.ToString()),
            EventKey: $"workflow-execution:{execution.Id}:paused"));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyWorkflowPausedAsync(userId, execution.Id, DateTime.UtcNow, cancellationToken);
    }
}
