using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpResources;

/// <summary>spec.md FR-036 — any authenticated user.</summary>
public sealed record ListAvailableMcpResourcesQuery : IRequest<IReadOnlyList<McpResourceCatalogSummaryDto>>;
