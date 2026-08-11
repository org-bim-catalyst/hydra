using AskLucy.Domain.Agents;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

public enum McpToolActivationStatus
{
    PendingReview,
    Active,
    Deactivated,
}

/// <summary>
/// Normalized tool metadata from one <see cref="McpCapabilitySnapshot"/> (spec.md FR-019-FR-026),
/// adapted into the existing <c>IAgentTool</c> contract at runtime via <c>McpToolAdapter</c>
/// (research.md Decision 1). Every tool starts <see cref="ActivationStatus"/>
/// <see cref="McpToolActivationStatus.PendingReview"/> regardless of what the server itself
/// declares for risk/permissions (clarification, FR-022) — <see cref="EffectiveRiskLevel"/>, never
/// <see cref="ServerDeclaredRiskLevel"/>, governs runtime behavior.
/// </summary>
public sealed class McpTool : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public Guid McpCapabilitySnapshotId { get; private set; }

    public string NamespacedName { get; private set; } = string.Empty;

    public string ToolName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string InputSchemaJson { get; private set; } = "{}";

    public string OutputSchemaJson { get; private set; } = "{}";

    public string? DeclaredCapabilitiesJson { get; private set; }

    public AgentToolRiskLevel? ServerDeclaredRiskLevel { get; private set; }

    public AgentToolRiskLevel EffectiveRiskLevel { get; private set; } = AgentToolRiskLevel.Critical;

    public string RequiredPermissionsJson { get; private set; } = "[]";

    public McpToolActivationStatus ActivationStatus { get; private set; } = McpToolActivationStatus.PendingReview;

    public string? ActivatedByUserId { get; private set; }

    public DateTime? ActivatedAtUtc { get; private set; }

    public string? Version { get; private set; }

    public bool IsAvailable { get; private set; } = true;

    private McpTool()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// Creates a newly-discovered (or re-discovered/changed) tool row — always
    /// <see cref="McpToolActivationStatus.PendingReview"/> (FR-022, contracts/mcp-lifecycle-events.md's
    /// re-review rule); <paramref name="carriedForwardActivation"/> lets the caller preserve a prior
    /// activation decision when the discovery diff shows this tool is unchanged from the last snapshot.
    /// </summary>
    public static McpTool CreateFromDiscovery(
        Guid mcpServerId,
        Guid mcpCapabilitySnapshotId,
        string toolName,
        string displayName,
        string description,
        string inputSchemaJson,
        string outputSchemaJson,
        string? declaredCapabilitiesJson,
        AgentToolRiskLevel? serverDeclaredRiskLevel,
        string requiredPermissionsJson,
        string? version,
        (McpToolActivationStatus Status, string? ActivatedByUserId, DateTime? ActivatedAtUtc)? carriedForwardActivation)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new DomainRuleViolationException("A tool name is required.");
        }

        var namespacedName = $"mcp:{mcpServerId}:{toolName}";
        if (namespacedName.Length > 400)
        {
            throw new DomainRuleViolationException("A tool's namespaced name must be 400 characters or fewer.");
        }

        var now = DateTime.UtcNow;
        var tool = new McpTool
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            McpCapabilitySnapshotId = mcpCapabilitySnapshotId,
            NamespacedName = namespacedName,
            ToolName = toolName,
            DisplayName = displayName,
            Description = description,
            InputSchemaJson = inputSchemaJson,
            OutputSchemaJson = outputSchemaJson,
            DeclaredCapabilitiesJson = declaredCapabilitiesJson,
            ServerDeclaredRiskLevel = serverDeclaredRiskLevel,
            EffectiveRiskLevel = serverDeclaredRiskLevel ?? AgentToolRiskLevel.Critical,
            RequiredPermissionsJson = requiredPermissionsJson,
            Version = version,
            IsAvailable = true,
            ActivationStatus = McpToolActivationStatus.PendingReview,
            CreatedAtUtc = now,
            CreatedBy = "system",
        };

        if (carriedForwardActivation is { } carried)
        {
            tool.ActivationStatus = carried.Status;
            tool.ActivatedByUserId = carried.ActivatedByUserId;
            tool.ActivatedAtUtc = carried.ActivatedAtUtc;
        }

        return tool;
    }

    public void Activate(string actor, AgentToolRiskLevel? effectiveRiskLevelOverride, string? requiredPermissionsJsonOverride)
    {
        ActivationStatus = McpToolActivationStatus.Active;
        ActivatedByUserId = actor;
        ActivatedAtUtc = DateTime.UtcNow;

        if (effectiveRiskLevelOverride is { } risk)
        {
            EffectiveRiskLevel = risk;
        }

        if (!string.IsNullOrWhiteSpace(requiredPermissionsJsonOverride))
        {
            RequiredPermissionsJson = requiredPermissionsJsonOverride;
        }

        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Deactivate(string actor)
    {
        ActivationStatus = McpToolActivationStatus.Deactivated;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MarkUnavailable()
    {
        IsAvailable = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = "system";
    }
}
