using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpTools;

public sealed class ListAvailableMcpToolsQueryHandler(IMcpToolRepository toolRepository)
    : IRequestHandler<ListAvailableMcpToolsQuery, IReadOnlyList<McpToolCatalogSummaryDto>>
{
    public async Task<IReadOnlyList<McpToolCatalogSummaryDto>> Handle(ListAvailableMcpToolsQuery request, CancellationToken cancellationToken)
    {
        var rows = await toolRepository.ListActiveAvailableAsync(cancellationToken);
        return rows.Select(r => McpToolCatalogSummaryDto.Create(r.Tool, r.ServerName)).ToList();
    }
}
