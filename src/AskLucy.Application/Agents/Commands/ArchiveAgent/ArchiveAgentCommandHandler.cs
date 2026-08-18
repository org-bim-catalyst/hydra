using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.ArchiveAgent;

public sealed class ArchiveAgentCommandHandler(IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<ArchiveAgentCommand, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(ArchiveAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        agent.Archive(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentDetailDto.Create(agent);
    }
}
