using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// Draft-time association between an <see cref="Agent"/> and a tool it may use (spec.md
/// FR-020, data-model.md). <see cref="ToolName"/> is a key into the compile-time
/// <c>IAgentTool</c> catalog (research.md Decision 10) — tools are code, not data, this release.
/// </summary>
public sealed class AgentTool : BaseEntity
{
    public Guid AgentId { get; private set; }

    public string ToolName { get; private set; } = string.Empty;

    public string? ConfigurationJson { get; private set; }

    private AgentTool()
    {
        // Required by EF Core materialization.
    }

    internal static AgentTool Create(Guid agentId, string toolName, string? configurationJson, string actor)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new DomainRuleViolationException("A tool name is required.");
        }

        return new AgentTool
        {
            Id = Guid.CreateVersion7(),
            AgentId = agentId,
            ToolName = toolName,
            ConfigurationJson = configurationJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
