using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServers;

public sealed class ListMcpServersQueryHandler(IMcpServerRepository serverRepository)
    : IRequestHandler<ListMcpServersQuery, PagedResult<McpServerDto>>
{
    public async Task<PagedResult<McpServerDto>> Handle(ListMcpServersQuery request, CancellationToken cancellationToken)
    {
        var (items, nextCursor) = await serverRepository.ListAsync(
            request.Status, request.Transport, request.Enabled, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<McpServerDto>(items.Select(McpServerDto.Create).ToList(), nextCursor);
    }
}
