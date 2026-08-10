using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgents;

public sealed class ListAgentsQueryHandler(IAgentRepository agentRepository, ICurrentUserAccessor currentUser)
    : IRequestHandler<ListAgentsQuery, PagedResult<AgentListItemDto>>
{
    public async Task<PagedResult<AgentListItemDto>> Handle(ListAgentsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await agentRepository.ListByOwnerAsync(
            userId, request.Status, request.AgentType, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<AgentListItemDto>(items.Select(AgentListItemDto.Create).ToList(), nextCursor);
    }
}
