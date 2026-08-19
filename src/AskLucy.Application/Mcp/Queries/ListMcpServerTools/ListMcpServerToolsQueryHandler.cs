using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServerTools;

public sealed class ListMcpServerToolsQueryHandler(IMcpToolRepository toolRepository)
    : IRequestHandler<ListMcpServerToolsQuery, IReadOnlyList<McpToolDto>>
{
    public async Task<IReadOnlyList<McpToolDto>> Handle(ListMcpServerToolsQuery request, CancellationToken cancellationToken)
    {
        var tools = await toolRepository.ListByServerIdAsync(request.Id, cancellationToken);

        return tools.Select(McpToolDto.Create).ToList();
    }
}
