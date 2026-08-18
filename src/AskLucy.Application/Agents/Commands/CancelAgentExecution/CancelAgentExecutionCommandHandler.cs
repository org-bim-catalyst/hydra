using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Domain.Agents;
using MediatR;

namespace AskLucy.Application.Agents.Commands.CancelAgentExecution;

public sealed class CancelAgentExecutionCommandHandler(
    IAgentExecutionRepository executionRepository, IAgentExecutionNotifier notifier, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<CancelAgentExecutionCommand>
{
    private const string Reason = "Cancelled by user.";

    public async Task Handle(CancelAgentExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        if (execution.Status is AgentExecutionStatus.Completed or AgentExecutionStatus.Failed or AgentExecutionStatus.Cancelled)
        {
            return; // Already terminal — nothing to cancel.
        }

        execution.Cancel(Reason);
        execution.RecordEvent(AgentExecutionEventType.ExecutionCancelled, execution.AgentVersionId, null, "Cancelled", null);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.NotifyExecutionCancelledAsync(userId, execution.Id, Reason, DateTime.UtcNow, cancellationToken);
    }
}
