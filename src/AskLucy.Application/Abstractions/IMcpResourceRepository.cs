using AskLucy.Domain.Mcp;

namespace AskLucy.Application.Abstractions;

public interface IMcpResourceRepository
{
    Task<McpResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<McpResource?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default);

    void Add(McpResource resource);

    Task<IReadOnlyList<McpResource>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>FR-036 catalog listing — includes each resource's source server name (mirrors <c>IMcpToolRepository.ListActiveAvailableAsync</c>'s shape).</summary>
    Task<IReadOnlyList<(McpResource Resource, string ServerName)>> ListAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>Same filter as <see cref="ListAvailableAsync"/> (available + source server enabled), scoped to one <c>NamespacedName</c> — backs <c>McpResourceReadTool</c>, so a disabled/removed server's resource is rejected exactly as if it weren't in the catalog either.</summary>
    Task<McpResource?> GetAvailableByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default);

    Task<McpResource?> GetLatestByServerAndUriAsync(Guid serverId, string resourceUri, CancellationToken cancellationToken = default);
}
