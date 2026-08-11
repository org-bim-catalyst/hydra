using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListMcpServerTools;

/// <summary>Admin view (contracts/mcp-api.md) — includes <c>PendingReview</c>/<c>Deactivated</c> tools, unlike the user-facing active-tool catalog.</summary>
public sealed record ListMcpServerToolsQuery(Guid Id) : IRequest<IReadOnlyList<McpToolDto>>;
