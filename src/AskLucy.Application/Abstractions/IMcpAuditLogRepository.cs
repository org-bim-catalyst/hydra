using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Abstractions;

public interface IMcpAuditLogRepository
{
    void Add(McpAuditLog entry);

    /// <summary>spec.md FR-058 — cursor-paginated, ordered by <c>OccurredAtUtc</c> descending, scoped to one server.</summary>
    Task<(IReadOnlyList<McpAuditLog> Items, string? NextCursor)> ListByServerAsync(
        Guid serverId, string? cursor, int pageSize, CancellationToken cancellationToken = default);
}
