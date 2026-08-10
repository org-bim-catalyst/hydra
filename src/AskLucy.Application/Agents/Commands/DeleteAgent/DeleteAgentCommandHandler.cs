using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.DeleteAgent;

public sealed class DeleteAgentCommandHandler(IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DeleteAgentCommand>
{
    public async Task Handle(DeleteAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        agent.SoftDelete(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
