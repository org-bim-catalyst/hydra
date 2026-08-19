using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class McpToolRepository(AskLucyDbContext dbContext) : IMcpToolRepository
{
    public Task<McpTool?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.McpTools.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<McpTool?> GetByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default) =>
        dbContext.McpTools.FirstOrDefaultAsync(t => t.NamespacedName == namespacedName, cancellationToken);

    public void Add(McpTool tool) => dbContext.McpTools.Add(tool);

    public async Task<IReadOnlyList<McpTool>> ListByServerIdAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        await dbContext.McpTools
            .Where(t => t.McpServerId == serverId)
            .OrderBy(t => t.ToolName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<(McpTool Tool, string ServerName)>> ListActiveAvailableAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ActiveAvailableQuery().ToListAsync(cancellationToken);
        return rows.Select(r => (r.tool, r.Name)).ToList();
    }

    public async Task<(McpTool Tool, string ServerName)?> GetActiveAvailableByNamespacedNameAsync(string namespacedName, CancellationToken cancellationToken = default)
    {
        var row = await ActiveAvailableQuery().FirstOrDefaultAsync(r => r.tool.NamespacedName == namespacedName, cancellationToken);
        return row is null ? null : (row.tool, row.Name);
    }

    private IQueryable<ActiveAvailableRow> ActiveAvailableQuery() =>
        from tool in dbContext.McpTools
        join server in dbContext.McpServers on tool.McpServerId equals server.Id
        join health in dbContext.McpServerHealths on server.Id equals health.McpServerId into healthJoin
        from health in healthJoin.DefaultIfEmpty()
        where tool.ActivationStatus == McpToolActivationStatus.Active
              && tool.IsAvailable
              && server.IsEnabled
              && (health == null || (health.Status != McpServerHealthStatus.Unavailable && health.Status != McpServerHealthStatus.AuthenticationFailed))
        select new ActiveAvailableRow(tool, server.Name);

    private sealed record ActiveAvailableRow(McpTool tool, string Name);

    public Task<McpTool?> GetLatestByServerAndToolNameAsync(Guid serverId, string toolName, CancellationToken cancellationToken = default) =>
        dbContext.McpTools
            .Where(t => t.McpServerId == serverId && t.ToolName == toolName)
            .OrderByDescending(t => t.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
