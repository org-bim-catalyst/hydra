using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Authorization;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;
using MediatR;

namespace AskLucy.Application.Agents.Commands.ResumeAgentExecution;

public sealed class ResumeAgentExecutionCommandHandler(
    IAgentExecutionRepository executionRepository, IAgentExecutionRunner runner, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ResumeAgentExecutionCommand>
{
    public async Task Handle(ResumeAgentExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        if (execution.Status != AgentExecutionStatus.Paused)
        {
            throw new DomainRuleViolationException("Only a paused execution can be resumed this way — a WaitingForApproval execution resumes via the approval decision instead.");
        }

        execution.Resume();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await runner.EnqueueAsync(execution.Id, cancellationToken);
    }
}
