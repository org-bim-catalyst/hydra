using AskLucy.Application.Agents.Tools;
using MediatR;

namespace AskLucy.Application.Agents.Queries.ListAgentTools;

/// <summary>
/// Reads the tool catalogue from <see cref="AgentToolCatalog"/> — the same merged set (native
/// tools plus the currently-active MCP tools) the agent runtime itself dispatches on, so the
/// admin view can never show a tool the runtime would refuse, or hide one it would use.
/// </summary>
public sealed class ListAgentToolsQueryHandler(AgentToolCatalog toolCatalog)
    : IRequestHandler<ListAgentToolsQuery, IReadOnlyList<AgentToolDto>>
{
    public Task<IReadOnlyList<AgentToolDto>> Handle(ListAgentToolsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentToolDto>>(
            [.. toolCatalog.All.Select(AgentToolDto.Create).OrderBy(t => t.Name, StringComparer.Ordinal)]);
}
