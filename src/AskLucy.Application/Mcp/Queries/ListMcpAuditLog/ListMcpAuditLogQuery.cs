using AskLucy.Application.Common;
using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpAuditLog;

/// <summary>spec.md FR-058 — cursor-paginated audit log for one server.</summary>
public sealed record ListMcpAuditLogQuery(Guid Id, string? Cursor, int PageSize) : IRequest<PagedResult<McpAuditLogDto>>;
