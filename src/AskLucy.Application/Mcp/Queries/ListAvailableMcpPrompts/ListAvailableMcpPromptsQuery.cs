using AskLucy.Application.Mcp;
using MediatR;

namespace AskLucy.Application.Mcp.Queries.ListAvailableMcpPrompts;

/// <summary>spec.md FR-042 — any authenticated user; MCP-sourced prompts only (research.md Decision 16 — merged with native prompts client-side wherever a unified prompt picker is shown).</summary>
public sealed record ListAvailableMcpPromptsQuery : IRequest<IReadOnlyList<McpPromptCatalogSummaryDto>>;
