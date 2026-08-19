using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

/// <summary>
/// A versioned, point-in-time record of everything an <see cref="McpServer"/> reported during one
/// discovery run (spec.md FR-011, FR-015-FR-018). Append-only — never updated after insert.
/// </summary>
public sealed class McpCapabilitySnapshot : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public DateTime DiscoveredAtUtc { get; private set; }

    public int SnapshotVersion { get; private set; }

    public string DeclaredCapabilitiesJson { get; private set; } = "[]";

    public string? ChangeSummaryJson { get; private set; }

    public bool WasSuccessful { get; private set; }

    public McpFailureCategory? FailureCategory { get; private set; }

    private McpCapabilitySnapshot()
    {
        // Required by EF Core materialization.
    }

    public static McpCapabilitySnapshot CreateSuccessful(
        Guid mcpServerId, int snapshotVersion, string declaredCapabilitiesJson, string? changeSummaryJson, string actor)
    {
        var now = DateTime.UtcNow;
        return new McpCapabilitySnapshot
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            DiscoveredAtUtc = now,
            SnapshotVersion = snapshotVersion,
            DeclaredCapabilitiesJson = declaredCapabilitiesJson,
            ChangeSummaryJson = changeSummaryJson,
            WasSuccessful = true,
            FailureCategory = null,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    public static McpCapabilitySnapshot CreateFailed(
        Guid mcpServerId, int snapshotVersion, McpFailureCategory failureCategory, string actor)
    {
        var now = DateTime.UtcNow;
        return new McpCapabilitySnapshot
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            DiscoveredAtUtc = now,
            SnapshotVersion = snapshotVersion,
            DeclaredCapabilitiesJson = "[]",
            ChangeSummaryJson = null,
            WasSuccessful = false,
            FailureCategory = failureCategory,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }
}
