using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Authorization;
using MediatR;

namespace AskLucy.Application.Workflows.Commands.RetryWorkflowExecutionNode;

public sealed class RetryWorkflowExecutionNodeCommandHandler(
    IWorkflowExecutionRepository executionRepository, IWorkflowExecutionRunner runner, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RetryWorkflowExecutionNodeCommand>
{
    public async Task Handle(RetryWorkflowExecutionNodeCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = WorkflowExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        var node = execution.Nodes.FirstOrDefault(n => n.Id == request.ExecutionNodeId)
            ?? throw new KeyNotFoundException("Workflow execution node not found.");

        node.ResetForRetry();
        execution.ReopenForRetry();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await runner.EnqueueAsync(execution.Id, cancellationToken);
    }
}
