using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

/// <summary>Normalized resource metadata from one <see cref="McpCapabilitySnapshot"/> (spec.md FR-036-FR-040).</summary>
public sealed class McpResource : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public Guid McpCapabilitySnapshotId { get; private set; }

    public string NamespacedName { get; private set; } = string.Empty;

    /// <summary>The resource's own protocol URI (e.g. <c>file:///data.txt</c>), passed verbatim to <c>IMcpClient.ReadResourceAsync</c> — distinct from <see cref="Name"/>, which is the server's human-readable label.</summary>
    public string Uri { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? ContentType { get; private set; }

    public bool IsAvailable { get; private set; } = true;

    private McpResource()
    {
        // Required by EF Core materialization.
    }

    public static McpResource CreateFromDiscovery(
        Guid mcpServerId, Guid mcpCapabilitySnapshotId, string resourceUri, string name, string? description, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new DomainRuleViolationException("A resource URI is required.");
        }

        var namespacedName = $"mcp:{mcpServerId}:{resourceUri}";
        if (namespacedName.Length > 400)
        {
            throw new DomainRuleViolationException("A resource's namespaced name must be 400 characters or fewer.");
        }

        return new McpResource
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            McpCapabilitySnapshotId = mcpCapabilitySnapshotId,
            NamespacedName = namespacedName,
            Uri = resourceUri,
            Name = name,
            Description = description,
            ContentType = contentType,
            IsAvailable = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system",
        };
    }

    public void MarkUnavailable()
    {
        IsAvailable = false;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = "system";
    }
}
