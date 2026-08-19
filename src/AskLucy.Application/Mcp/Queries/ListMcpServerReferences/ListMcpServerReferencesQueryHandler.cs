using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServerReferences;

public sealed class ListMcpServerReferencesQueryHandler(IMcpServerRepository serverRepository)
    : IRequestHandler<ListMcpServerReferencesQuery, IReadOnlyList<McpServerReferenceDto>>
{
    public async Task<IReadOnlyList<McpServerReferenceDto>> Handle(ListMcpServerReferencesQuery request, CancellationToken cancellationToken)
    {
        var references = await serverRepository.ListReferencingAgentToolsAsync(request.Id, cancellationToken);

        return references.Select(r => new McpServerReferenceDto(r.AgentId, r.ToolName)).ToList();
    }
}
