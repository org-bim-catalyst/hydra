namespace AskLucy.Application.Agents.Tools;

/// <summary>Resolves a registered <see cref="IAgentTool"/> by name (research.md Decision 10) — the Agent Runtime's only way to discover/invoke a tool. No dynamic/runtime discovery: the catalog is a fixed, DI-registered set built at container-composition time.</summary>
public sealed class AgentToolCatalog(IEnumerable<IAgentTool> tools)
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _toolsByName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public IAgentTool? Find(string toolName) => _toolsByName.GetValueOrDefault(toolName);

    public IReadOnlyCollection<IAgentTool> All => _toolsByName.Values.ToList();
}
