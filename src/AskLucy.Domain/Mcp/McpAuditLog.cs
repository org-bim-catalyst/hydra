using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

public enum McpAuditAction
{
    ServerRegistered,
    ServerUpdated,
    ServerEnabled,
    ServerDisabled,
    ServerRemovalBlocked,
    ServerRemoved,
    CredentialRotated,
    CapabilityDiscoveryStarted,
    CapabilityDiscoverySucceeded,
    CapabilityDiscoveryFailed,
    HealthStateChanged,
    ToolActivated,
    ToolDeactivated,
    UnauthorizedAccessAttempted,
}

/// <summary>
/// Tamper-resistant record of MCP-specific administrative/security events (spec.md FR-058-FR-060).
/// Deliberately not hard-FK'd to <see cref="McpServer"/> (mirrors <c>AgentAuditLog</c>'s existing
/// pattern) — an audit entry for a later-removed server is retained. Append-only.
/// </summary>
public sealed class McpAuditLog : BaseEntity
{
    public Guid? McpServerId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public McpAuditAction Action { get; private set; }

    public McpFailureCategory? FailureCategory { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public DateTime OccurredAtUtc { get; private set; }

    private McpAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static McpAuditLog Record(
        Guid? mcpServerId, string userId, McpAuditAction action, McpFailureCategory? failureCategory, string detailsJson)
    {
        var now = DateTime.UtcNow;
        return new McpAuditLog
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            UserId = userId,
            Action = action,
            FailureCategory = failureCategory,
            DetailsJson = detailsJson,
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            CreatedBy = userId,
        };
    }
}
