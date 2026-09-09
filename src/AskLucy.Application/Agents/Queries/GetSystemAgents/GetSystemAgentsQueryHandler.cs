using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Agents.Queries.GetSystemAgents;

public sealed class GetSystemAgentsQueryHandler(IAgentRepository agents)
    : IRequestHandler<GetSystemAgentsQuery, IReadOnlyList<AdminSystemAgentDto>>
{
    public async Task<IReadOnlyList<AdminSystemAgentDto>> Handle(GetSystemAgentsQuery request, CancellationToken cancellationToken)
    {
        var systemAgents = await agents.ListSystemOwnedAsync(cancellationToken);

        return [.. systemAgents.Select(AdminSystemAgentDto.FromEntity)];
    }
}
