using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Abstractions;

public interface IMcpToolRepository
{
    Task<McpTool?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<McpTool?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default);

    void Add(McpTool tool);

    /// <summary>Admin view (contracts/mcp-api.md) — includes <c>PendingReview</c>/<c>Deactivated</c> tools, unlike <see cref="ListActiveAvailableAsync"/>.</summary>
    Task<IReadOnlyList<McpTool>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// research.md Decision 1/4 — the exact filter <c>IMcpToolRegistry.ActiveTools</c> applies:
    /// <c>ActivationStatus == Active</c>, <c>IsAvailable == true</c>, source server enabled and not
    /// <c>Unavailable</c>/<c>AuthenticationFailed</c>. Also returns each tool's source server name
    /// — FR-029's "target MCP server" requirement is satisfied by <c>McpToolAdapter</c> embedding
    /// this in its <c>Description</c> at construction time, not by the (deliberately MCP-agnostic)
    /// <c>AgentExecutionOrchestrator</c> resolving it per-approval.
    /// </summary>
    Task<IReadOnlyList<(McpTool Tool, string ServerName)>> ListActiveAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Same filter as <see cref="ListActiveAvailableAsync"/>, scoped to one <c>NamespacedName</c> — backs the user-facing catalog's detail view (<c>GetMcpToolQuery</c>), so a non-active/unavailable tool 404s exactly as if it weren't in the catalog list either.</summary>
    Task<(McpTool Tool, string ServerName)?> GetActiveAvailableByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default);

    /// <summary>Used by discovery (contracts/mcp-lifecycle-events.md's carry-forward rule) to find the immediately-prior row for the same tool name on the same server, across snapshots.</summary>
    Task<McpTool?> GetLatestByServerAndToolNameAsync(Guid serverId, string toolName, CancellationToken cancellationToken = default);
}
