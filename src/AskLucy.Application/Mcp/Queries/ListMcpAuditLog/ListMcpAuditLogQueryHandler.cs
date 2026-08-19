using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpAuditLog;

public sealed class ListMcpAuditLogQueryHandler(IMcpAuditLogRepository auditLogRepository)
    : IRequestHandler<ListMcpAuditLogQuery, PagedResult<McpAuditLogDto>>
{
    public async Task<PagedResult<McpAuditLogDto>> Handle(ListMcpAuditLogQuery request, CancellationToken cancellationToken)
    {
        var (items, nextCursor) = await auditLogRepository.ListByServerAsync(request.Id, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<McpAuditLogDto>(items.Select(McpAuditLogDto.Create).ToList(), nextCursor);
    }
}
