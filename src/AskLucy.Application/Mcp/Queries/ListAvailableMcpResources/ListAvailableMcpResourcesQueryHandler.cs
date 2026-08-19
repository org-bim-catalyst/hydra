using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpResources;

public sealed class ListAvailableMcpResourcesQueryHandler(IMcpResourceRepository resourceRepository)
    : IRequestHandler<ListAvailableMcpResourcesQuery, IReadOnlyList<McpResourceCatalogSummaryDto>>
{
    public async Task<IReadOnlyList<McpResourceCatalogSummaryDto>> Handle(ListAvailableMcpResourcesQuery request, CancellationToken cancellationToken)
    {
        var rows = await resourceRepository.ListAvailableAsync(cancellationToken);
        return rows.Select(r => McpResourceCatalogSummaryDto.Create(r.Resource, r.ServerName)).ToList();
    }
}
