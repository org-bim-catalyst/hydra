using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgentVersion;

public sealed class GetAgentVersionQueryHandler(IAgentRepository agentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentVersionQuery, AgentVersionDto>
{
    public async Task<AgentVersionDto> Handle(GetAgentVersionQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.AgentId, userId, cancellationToken), userId);

        var version = await agentRepository.GetVersionAsync(request.AgentId, request.VersionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Agent version not found.");

        return AgentVersionDto.Create(version);
    }
}
