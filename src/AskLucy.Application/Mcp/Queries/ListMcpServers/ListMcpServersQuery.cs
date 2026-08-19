using AskLucy.Application.Common;
using AskLucy.Application.Mcp;
using AskLucy.Domain.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServers;

/// <summary>contracts/mcp-api.md — cursor-paginated admin server list; <paramref name="Status"/> filters against each server's current <see cref="McpServerHealth"/> row.</summary>
public sealed record ListMcpServersQuery(
    McpServerHealthStatus? Status, McpServerTransport? Transport, bool? Enabled, string? Cursor, int PageSize) : IRequest<PagedResult<McpServerDto>>;
