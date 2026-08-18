using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.RestoreAgent;

public sealed class RestoreAgentCommandHandler(IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RestoreAgentCommand, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(RestoreAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        agent.Restore(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentDetailDto.Create(agent);
    }
}
