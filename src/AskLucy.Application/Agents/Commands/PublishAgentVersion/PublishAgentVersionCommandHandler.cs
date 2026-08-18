using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Commands.PublishAgentVersion;

public sealed class PublishAgentVersionCommandHandler(
    IAgentRepository agentRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<PublishAgentVersionCommand, AgentVersionDto>
{
    public async Task<AgentVersionDto> Handle(PublishAgentVersionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.AgentId, userId, cancellationToken), userId);

        var version = agent.Publish(request.ChangeDescription, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AgentVersionDto.Create(version);
    }
}
