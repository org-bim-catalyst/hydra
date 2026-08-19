namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// The in-memory, singleton-scoped snapshot of every currently-active MCP tool, exposed as plain
/// <see cref="IAgentTool"/> instances (research.md Decision 1/4) — <see cref="AgentToolCatalog"/>
/// merges this with the native DI-registered tool set so the Agent Runtime never branches on
/// native-vs-MCP (FR-019).
/// </summary>
public interface IMcpToolRegistry
{
    /// <summary>Synchronous, in-memory read of the last successfully built snapshot — never triggers I/O itself (constitution §4, no blocking-on-Task). Momentarily stale immediately after an activation/deactivation/health change until the corresponding command's <see cref="InvalidateAsync"/> call completes.</summary>
    IReadOnlyCollection<IAgentTool> ActiveTools { get; }

    /// <summary>
    /// Rebuilds <see cref="ActiveTools"/> from the current database state. Called by every command
    /// that changes which tools should be active (activate/deactivate a tool, enable/disable a
    /// server, a health-check transition) — awaited by the caller before it returns, so a
    /// subsequent read of <see cref="ActiveTools"/> is guaranteed current.
    /// </summary>
    Task InvalidateAsync(CancellationToken cancellationToken = default);
}
