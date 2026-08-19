using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.GetMcpTool;

/// <summary>spec.md FR-020 — full detail for one catalog tool, looked up by its namespaced name; scoped to the same Active/Available/enabled/healthy filter as <c>ListAvailableMcpToolsQuery</c> (404 otherwise).</summary>
public sealed record GetMcpToolQuery(string NamespacedName) : IRequest<McpToolDetailDto>;
