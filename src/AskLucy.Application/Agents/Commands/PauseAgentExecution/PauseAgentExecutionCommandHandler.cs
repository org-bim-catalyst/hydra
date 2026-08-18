using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.PauseAgentExecution;

public sealed class PauseAgentExecutionCommandHandler(
    IAgentExecutionRepository executionRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<PauseAgentExecutionCommand>
{
    public async Task Handle(PauseAgentExecutionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var execution = AgentExecutionOwnershipGuard.EnsureOwnedBy(
            await executionRepository.GetByIdForUserAsync(request.ExecutionId, userId, cancellationToken), userId);

        execution.Pause();
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
