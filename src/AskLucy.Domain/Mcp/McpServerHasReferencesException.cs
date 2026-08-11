namespace AskLucy.Domain.Mcp;

/// <summary>
/// Thrown when an administrator attempts to remove an <see cref="McpServer"/> that one or more
/// agents still reference (spec.md FR-005, clarification, research.md Decision 15) — mapped to
/// <c>422 Unprocessable Entity</c>. <see cref="ReferencingAgentTools"/> lists every
/// <c>(AgentId, ToolName)</c> pair that must be cleared first.
/// </summary>
public sealed class McpServerHasReferencesException(IReadOnlyList<(Guid AgentId, string ToolName)> referencingAgentTools)
    : Exception($"This server cannot be removed: {referencingAgentTools.Count} agent tool reference(s) still point to it. Remove them from their owning agent(s) first.")
{
    public IReadOnlyList<(Guid AgentId, string ToolName)> ReferencingAgentTools { get; } = referencingAgentTools;
}
