using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

/// <summary>
/// One administrator-set value for one of an agent tool's own settings — e.g. which imagery
/// provider the map snapshot tool uses by default, or how many pixels it crops from each edge.
/// <para>
/// At most one row per tool and key; a setting with no row takes the default its definition
/// declares, so a tool never needs a row to work.
/// </para>
/// <para>
/// The value is stored as text rather than one column per type: which settings exist, and what
/// type each has, is declared by the Application layer's catalog, and adding a setting must not
/// need a migration. The owner is the tool's own name (e.g. <c>map_snapshot</c>) rather than an
/// enum, because the set of tools is open — an MCP server contributes its own — and a new tool
/// must not need a code change to be configurable.
/// </para>
/// </summary>
public sealed class ToolSetting : BaseEntity
{
    public const int MaxToolNameLength = 100;
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 500;

    private ToolSetting() { }

    /// <summary>The tool this setting belongs to — its <c>IAgentTool.Name</c>.</summary>
    public string ToolName { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public static ToolSetting Create(string toolName, string key, string value, string actor)
    {
        EnsureValid(toolName, key, value);

        return new ToolSetting
        {
            Id = Guid.CreateVersion7(),
            ToolName = toolName,
            Key = key,
            Value = value,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Change(string value, string actor)
    {
        EnsureValid(ToolName, Key, value);

        Value = value;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    private static void EnsureValid(string toolName, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(toolName) || toolName.Length > MaxToolNameLength)
        {
            throw new DomainRuleViolationException($"A tool setting's tool name must be 1 to {MaxToolNameLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
        {
            throw new DomainRuleViolationException($"A tool setting key must be 1 to {MaxKeyLength} characters.");
        }

        if (value is null || value.Length > MaxValueLength)
        {
            throw new DomainRuleViolationException($"A tool setting value must be at most {MaxValueLength} characters.");
        }
    }
}
