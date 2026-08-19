using AskLucy.Application.Abstractions;
using AskLucy.Domain.Mcp;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class McpAuditLogRepository(AskLucyDbContext dbContext) : IMcpAuditLogRepository
{
    private const int MaxPageSize = 200;

    public void Add(McpAuditLog entry) => dbContext.McpAuditLogs.Add(entry);

    public async Task<(IReadOnlyList<McpAuditLog> Items, string? NextCursor)> ListByServerAsync(
        Guid serverId, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.McpAuditLogs.Where(a => a.McpServerId == serverId);

        var decoded = McpCursor.Decode(cursor);
        var sorted = query
            .Select(a => new { Entry = a, SortValue = a.OccurredAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Entry.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Entry.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Entry.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = McpCursor.Encode(last.SortValue, last.Entry.Id);
        }

        return (items.Select(x => x.Entry).ToList(), nextCursor);
    }
}
