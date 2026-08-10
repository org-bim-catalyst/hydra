using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentVersions;

public sealed class ListAgentVersionsQueryHandler(IAgentRepository agentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListAgentVersionsQuery, IReadOnlyList<AgentVersionDto>>
{
    public async Task<IReadOnlyList<AgentVersionDto>> Handle(ListAgentVersionsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.AgentId, userId, cancellationToken), userId);

        var versions = await agentRepository.ListVersionsAsync(agent.Id, cancellationToken);
        return versions.OrderByDescending(v => v.VersionNumber).Select(AgentVersionDto.Create).ToList();
    }
}
