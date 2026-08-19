using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpTools;

/// <summary>spec.md FR-062 — any authenticated user; filters identically to <c>IMcpToolRegistry.ActiveTools</c>, so what a user sees here is exactly what an agent can actually call.</summary>
public sealed record ListAvailableMcpToolsQuery : IRequest<IReadOnlyList<McpToolCatalogSummaryDto>>;
