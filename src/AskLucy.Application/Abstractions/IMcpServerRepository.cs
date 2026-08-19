using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Repository for the <see cref="McpServer"/> aggregate and its tightly-coupled children
/// (<see cref="McpServerCredential"/>, <see cref="McpServerHealth"/>, <see cref="McpCapabilitySnapshot"/>)
/// — mirrors <c>IAgentPolicyRepository</c>'s "one repository per related cluster" shape
/// (constitution §3 Repository rules). The admin server list (<see cref="ListAsync"/>) also lives
/// here rather than a raw <c>AskLucyDbContext</c> query handler, since its <c>status</c> filter is a
/// join against this same cluster's <see cref="McpServerHealth"/> child.
/// </summary>
public interface IMcpServerRepository
{
    Task<McpServer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(McpServer server);

    Task<McpServer?> GetByEndpointAndTransportAsync(string endpoint, McpServerTransport transport, CancellationToken cancellationToken = default);

    /// <summary>Admin server list (contracts/mcp-api.md) — cursor-paginated, ordered by <c>CreatedAtUtc</c> descending; <paramref name="status"/> filters against this server's current <see cref="McpServerHealth"/> row (a server with no health row yet never matches a non-null filter).</summary>
    Task<(IReadOnlyList<McpServer> Items, string? NextCursor)> ListAsync(
        McpServerHealthStatus? status, McpServerTransport? transport, bool? enabled, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>research.md Decision 15 — joins <c>AgentTool.ToolName == McpTool.NamespacedName</c> where <c>McpTool.McpServerId == serverId</c>.</summary>
    Task<int> CountReferencingAgentToolsAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Same join as <see cref="CountReferencingAgentToolsAsync"/>, returning the actual references (FR-005/FR-065).</summary>
    Task<IReadOnlyList<(Guid AgentId, string ToolName)>> ListReferencingAgentToolsAsync(Guid serverId, CancellationToken cancellationToken = default);

    Task<McpServerCredential?> GetCredentialAsync(Guid serverId, CancellationToken cancellationToken = default);

    void AddCredential(McpServerCredential credential);

    Task<McpServerHealth?> GetHealthAsync(Guid serverId, CancellationToken cancellationToken = default);

    void AddHealth(McpServerHealth health);

    /// <summary>Backs <c>McpServerHealthCheckJob</c> (US6) — every currently-enabled server, checked on the recurring health-check cadence regardless of individual <c>CapabilityRefreshIntervalMinutes</c>.</summary>
    Task<IReadOnlyList<Guid>> ListEnabledServerIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Backs <c>McpCapabilityRefreshJob</c> (US6, FR-013) — every enabled server whose <c>LastCapabilityDiscoveryAtUtc</c> is null or older than its own <c>CapabilityRefreshIntervalMinutes</c>.</summary>
    Task<IReadOnlyList<Guid>> ListServersDueForCapabilityRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    /// <summary>0 when no snapshot exists yet — the caller's next snapshot version is this value + 1.</summary>
    Task<int> GetLatestCapabilitySnapshotVersionAsync(Guid serverId, CancellationToken cancellationToken = default);

    void AddCapabilitySnapshot(McpCapabilitySnapshot snapshot);
}
