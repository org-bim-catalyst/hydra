using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

/// <summary>
/// Normalized, read-only prompt metadata from one <see cref="McpCapabilitySnapshot"/> (spec.md
/// FR-041-FR-044, research.md Decision 16, clarification). Deliberately has no direct-edit method —
/// the only mutation path is <see cref="RefreshFromSnapshot"/>, called on every successful capability
/// refresh; a user who wants a customized copy duplicates it into an independent native prompt
/// (<c>DuplicateMcpPromptCommand</c>), which this entity has no further relationship to.
/// </summary>
public sealed class McpPrompt : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public Guid McpCapabilitySnapshotId { get; private set; }

    public string NamespacedName { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string ContentTemplate { get; private set; } = string.Empty;

    public bool IsAvailable { get; private set; } = true;

    private McpPrompt()
    {
        // Required by EF Core materialization.
    }

    public static McpPrompt CreateFromDiscovery(
        Guid mcpServerId, Guid mcpCapabilitySnapshotId, string promptName, string? description, string contentTemplate)
    {
        if (string.IsNullOrWhiteSpace(promptName))
        {
            throw new DomainRuleViolationException("A prompt name is required.");
        }

        var namespacedName = $"mcp:{mcpServerId}:{promptName}";
        if (namespacedName.Length > 400)
        {
            throw new DomainRuleViolationException("A prompt's namespaced name must be 400 characters or fewer.");
        }

        return new McpPrompt
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            McpCapabilitySnapshotId = mcpCapabilitySnapshotId,
            NamespacedName = namespacedName,
            Name = promptName,
            Description = description,
            ContentTemplate = contentTemplate,
            IsAvailable = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system",
        };
    }

    public void RefreshFromSnapshot(Guid mcpCapabilitySnapshotId, string? description, string contentTemplate)
    {
        McpCapabilitySnapshotId = mcpCapabilitySnapshotId;
        ContentTemplate = contentTemplate;
        Description = description;
        IsAvailable = true;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = "system";
    }

    public void MarkUnavailable()
    {
        IsAvailable = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = "system";
    }
}
