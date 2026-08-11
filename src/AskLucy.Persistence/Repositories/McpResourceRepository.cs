using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class McpResourceRepository(AskLucyDbContext dbContext) : IMcpResourceRepository
{
    public Task<McpResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.McpResources.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<McpResource?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default) =>
        dbContext.McpResources.FirstOrDefaultAsync(r => r.NamespacedName == namespacedName, cancellationToken);

    public void Add(McpResource resource) => dbContext.McpResources.Add(resource);

    public async Task<IReadOnlyList<McpResource>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        await dbContext.McpResources
            .Where(r => r.McpServerId == serverId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(McpResource Resource, string ServerName)>> ListAvailableAsync(CancellationToken cancellationToken = default)
    {
        var rows = await (from resource in AvailableQuery()
                          join server in dbContext.McpServers on resource.McpServerId equals server.Id
                          select new { resource, server.Name })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.resource, r.Name)).ToList();
    }

    public Task<McpResource?> GetAvailableByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default) =>
        AvailableQuery().FirstOrDefaultAsync(r => r.NamespacedName == namespacedName, cancellationToken);

    private IQueryable<McpResource> AvailableQuery() =>
        from resource in dbContext.McpResources
        join server in dbContext.McpServers on resource.McpServerId equals server.Id
        where resource.IsAvailable && server.IsEnabled
        select resource;

    public Task<McpResource?> GetLatestByServerAndUriAsync(Guid serverId, string resourceUri, CancellationToken cancellationToken = default) =>
        dbContext.McpResources
            .Where(r => r.McpServerId == serverId && r.Uri == resourceUri)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
