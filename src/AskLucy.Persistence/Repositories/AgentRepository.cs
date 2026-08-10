using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AgentRepository(AskLucyDbContext dbContext) : IAgentRepository
{
    private const int MaxPageSize = 200;

    public Task<Agent?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default) =>
        dbContext.Agents
            .Include(a => a.Tools)
            .Include(a => a.KnowledgeBases)
            .Include(a => a.MemoryPolicy)
            .FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == ownerId, cancellationToken);

    public Task<Agent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Agents
            .Include(a => a.Tools)
            .Include(a => a.KnowledgeBases)
            .Include(a => a.MemoryPolicy)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public void Add(Agent agent) => dbContext.Agents.Add(agent);

    public async Task<(IReadOnlyList<Agent> Items, string? NextCursor)> ListByOwnerAsync(
        string ownerId, AgentStatus? status, AgentType? agentType, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.Agents.Where(a => a.OwnerId == ownerId);

        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }

        if (agentType is not null)
        {
            query = query.Where(a => a.AgentType == agentType);
        }

        var decoded = AgentCursor.Decode(cursor);
        var sorted = query
            .Select(a => new { Agent = a, SortValue = a.ModifiedAtUtc ?? a.CreatedAtUtc })
            .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Agent.Id);

        if (decoded is { } d)
        {
            sorted = sorted
                .Where(x => x.SortValue < d.SortValue || (x.SortValue == d.SortValue && x.Agent.Id != d.Id))
                .OrderByDescending(x => x.SortValue).ThenByDescending(x => x.Agent.Id);
        }

        var page = await sorted.Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var items = (hasMore ? page.Take(pageSize) : page).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = items[^1];
            nextCursor = AgentCursor.Encode(last.SortValue, last.Agent.Id);
        }

        return (items.Select(x => x.Agent).ToList(), nextCursor);
    }

    public Task<AgentVersion?> GetVersionAsync(Guid agentId, int versionNumber, CancellationToken cancellationToken = default) =>
        dbContext.AgentVersions.FirstOrDefaultAsync(v => v.AgentId == agentId && v.VersionNumber == versionNumber, cancellationToken);

    public Task<AgentVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        dbContext.AgentVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);

    public async Task<IReadOnlyList<AgentVersion>> ListVersionsAsync(Guid agentId, CancellationToken cancellationToken = default) =>
        await dbContext.AgentVersions
            .Where(v => v.AgentId == agentId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
}
