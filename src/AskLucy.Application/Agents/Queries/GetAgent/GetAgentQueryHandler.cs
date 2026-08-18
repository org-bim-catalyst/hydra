using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Agents.Authorization;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetAgent;

public sealed class GetAgentQueryHandler(IAgentRepository agentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<GetAgentQuery, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(GetAgentQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var agent = AgentOwnershipGuard.EnsureOwnedBy(await agentRepository.GetByIdForOwnerAsync(request.Id, userId, cancellationToken), userId);

        return AgentDetailDto.Create(agent);
    }
}
