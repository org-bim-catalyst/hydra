using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class McpServerRepository(AskLucyDbContext dbContext) : IMcpServerRepository
{
    private const int MaxPageSize = 200;

    public Task<McpServer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.McpServers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public void Add(McpServer server) => dbContext.McpServers.Add(server);

    public Task<McpServer?> GetByEndpointAndTransportAsync(string endpoint, McpServerTransport transport, CancellationToken cancellationToken = default) =>
        dbContext.McpServers.FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.Transport == transport, cancellationToken);

    public async Task<(IReadOnlyList<McpServer> Items, string? NextCursor)> ListAsync(
        McpServerHealthStatus? status, McpServerTransport? transport, bool? enabled, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.McpServers.AsQueryable();

        if (transport is not null)
        {
            query = query.Where(s => s.Transport == transport);
        }

        if (enabled is not null)
        {
            query = query.Where(s => s.IsEnabled == enabled);
        }

        if (status is not null)
        {
            var matchingServerIds = dbContext.McpServerHealths.Where(h => h.Status == status).Select(h => h.McpServerId);
            query = query.Where(s => matchingServerIds.Contains(s.Id));
        }

        var decoded = McpCursor.Decode(cursor);
        var sorted = query
            .Select(s => new { Server = s, SortValue = s.CreatedAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Server.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Server.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Server.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = McpCursor.Encode(last.SortValue, last.Server.Id);
        }

        return (items.Select(x => x.Server).ToList(), nextCursor);
    }

    public Task<int> CountReferencingAgentToolsAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        (from agentTool in dbContext.AgentTools
         join mcpTool in dbContext.McpTools on agentTool.ToolName equals mcpTool.NamespacedName
         where mcpTool.McpServerId == serverId
         select agentTool.Id)
        .CountAsync(cancellationToken);

    public async Task<IReadOnlyList<(Guid AgentId, string ToolName)>> ListReferencingAgentToolsAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        await (from agentTool in dbContext.AgentTools
               join mcpTool in dbContext.McpTools on agentTool.ToolName equals mcpTool.NamespacedName
               where mcpTool.McpServerId == serverId
               select new ValueTuple<Guid, string>(agentTool.AgentId, agentTool.ToolName))
            .ToListAsync(cancellationToken);

    public Task<McpServerCredential?> GetCredentialAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        dbContext.McpServerCredentials.FirstOrDefaultAsync(c => c.McpServerId == serverId, cancellationToken);

    public void AddCredential(McpServerCredential credential) => dbContext.McpServerCredentials.Add(credential);

    public Task<McpServerHealth?> GetHealthAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        dbContext.McpServerHealths.FirstOrDefaultAsync(h => h.McpServerId == serverId, cancellationToken);

    public void AddHealth(McpServerHealth health) => dbContext.McpServerHealths.Add(health);

    public async Task<IReadOnlyList<Guid>> ListEnabledServerIdsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.McpServers.Where(s => s.IsEnabled).Select(s => s.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListServersDueForCapabilityRefreshAsync(DateTime utcNow, CancellationToken cancellationToken = default) =>
        await dbContext.McpServers
            .Where(s => s.IsEnabled && (s.LastCapabilityDiscoveryAtUtc == null || s.LastCapabilityDiscoveryAtUtc.Value.AddMinutes(s.CapabilityRefreshIntervalMinutes) <= utcNow))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task<int> GetLatestCapabilitySnapshotVersionAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        await dbContext.McpCapabilitySnapshots
            .Where(s => s.McpServerId == serverId)
            .Select(s => (int?)s.SnapshotVersion)
            .MaxAsync(cancellationToken) ?? 0;

    public void AddCapabilitySnapshot(McpCapabilitySnapshot snapshot) => dbContext.McpCapabilitySnapshots.Add(snapshot);
}
