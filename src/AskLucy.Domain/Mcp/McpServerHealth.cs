using AskLucy.Domain.Common;

namespace AskLucy.Domain.Mcp;

public enum McpServerHealthStatus
{
    Healthy,
    Degraded,
    Unavailable,
    AuthenticationFailed,
    ConfigurationError,
    Unknown,
}

public enum McpFailureCategory
{
    ConnectionFailure,
    AuthenticationFailure,
    AuthorizationFailure,
    Timeout,
    RateLimit,
    InvalidRequest,
    InvalidResponse,
    ServerError,
    ProtocolError,
    CapabilityDiscoveryFailure,
    ServerUnavailable,
}

/// <summary>
/// Current health state of an <see cref="McpServer"/> (spec.md FR-055/FR-056), one row per
/// server, overwritten on every check (research.md Decision 10). Historical trail lives in
/// <see cref="McpAuditLog"/>, not a second unbounded history table on this entity.
/// </summary>
public sealed class McpServerHealth : BaseEntity
{
    public Guid McpServerId { get; private set; }

    public McpServerHealthStatus Status { get; private set; } = McpServerHealthStatus.Unknown;

    public McpFailureCategory? FailureCategory { get; private set; }

    public string? Detail { get; private set; }

    public DateTime CheckedAtUtc { get; private set; }

    public int ConsecutiveFailureCount { get; private set; }

    private McpServerHealth()
    {
        // Required by EF Core materialization.
    }

    public static McpServerHealth CreateUnknown(Guid mcpServerId)
    {
        var now = DateTime.UtcNow;
        return new McpServerHealth
        {
            Id = Guid.CreateVersion7(),
            McpServerId = mcpServerId,
            Status = McpServerHealthStatus.Unknown,
            CheckedAtUtc = now,
            ConsecutiveFailureCount = 0,
            CreatedAtUtc = now,
            CreatedBy = "system",
        };
    }

    public void RecordCheck(McpServerHealthStatus status, McpFailureCategory? failureCategory, string? detail)
    {
        Status = status;
        FailureCategory = status == McpServerHealthStatus.Healthy ? null : failureCategory;
        Detail = detail;
        CheckedAtUtc = DateTime.UtcNow;
        ConsecutiveFailureCount = status == McpServerHealthStatus.Healthy ? 0 : ConsecutiveFailureCount + 1;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = "system";
    }
}
