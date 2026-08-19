using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.Options;

/// <summary>
/// Bound from configuration (constitution §4). System-wide, administrator-tunable defaults for
/// the MCP integration (spec.md Assumptions — "default numeric limits... are system-provided,
/// administrator-tunable defaults").
/// </summary>
public sealed class McpRuntimeOptions
{
    public const string SectionName = "Mcp";

    /// <summary>FR-009/FR-010 — local (stdio) server registration is rejected unless this deployment-level flag is explicitly enabled.</summary>
    public bool AllowLocalTransport { get; init; }

    [Range(1, 1440)]
    public int DefaultCapabilityRefreshIntervalMinutes { get; init; } = 60;

    /// <summary>FR-051 — independent of schema conformance.</summary>
    [Range(1024, 100_000_000)]
    public long MaxResponseSizeBytes { get; init; } = 5_000_000;

    [Range(1, 600)]
    public int MaxCallDurationSeconds { get; init; } = 30;

    [Range(0, 20)]
    public int MaxRetries { get; init; } = 3;

    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    [Range(1, 1440)]
    public int HealthCheckIntervalMinutes { get; init; } = 5;

    /// <summary>FR-052 — maximum simultaneous requests to a single MCP server.</summary>
    [Range(1, 1000)]
    public int MaxConcurrentRequestsPerServer { get; init; } = 10;

    /// <summary>FR-053 — requests per rolling one-minute window, per (server, tool, user, agent) key.</summary>
    [Range(1, 10_000)]
    public int MaxRequestsPerMinute { get; init; } = 60;
}
