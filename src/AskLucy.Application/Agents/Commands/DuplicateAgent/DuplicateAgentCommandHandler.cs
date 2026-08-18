using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.DuplicateAgent;

public sealed class DuplicateAgentCommandHandler(IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<DuplicateAgentCommand, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(DuplicateAgentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        var copy = agent.Duplicate(userId);
        agentRepository.Add(copy);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentDetailDto.Create(copy);
    }
}
