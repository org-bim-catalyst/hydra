namespace AskLucy.Application.Agents.Tools;

/// <summary>
/// Resolves a registered <see cref="IAgentTool"/> by name — the Agent Runtime's only way to
/// discover/invoke a tool. Merges two sources (spec 021-mcp-integration, research.md Decision 1):
/// the fixed, DI-registered native tool set (spec 020, research.md Decision 10) and
/// <see cref="IMcpToolRegistry"/>'s dynamic, currently-active MCP tools. The two sets are disjoint
/// by construction — every native tool name is a plain identifier, every MCP tool name is always
/// namespaced <c>mcp:{serverId}:{toolName}</c> (research.md Decision 3) — so no collision-handling
/// branch is needed. <see cref="Find"/>/<see cref="All"/> read <see cref="IMcpToolRegistry.ActiveTools"/>
/// live on every call, so a change reflects the very next lookup.
/// </summary>
public sealed class AgentToolCatalog(IEnumerable<IAgentTool> nativeTools, IMcpToolRegistry mcpToolRegistry)
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _nativeToolsByName = nativeTools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public IAgentTool? Find(string toolName) =>
        _nativeToolsByName.GetValueOrDefault(toolName)
        ?? mcpToolRegistry.ActiveTools.FirstOrDefault(t => t.Name == toolName);

    public IReadOnlyCollection<IAgentTool> All => [.. _nativeToolsByName.Values, .. mcpToolRegistry.ActiveTools];
}
