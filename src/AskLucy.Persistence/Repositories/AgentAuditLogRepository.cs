using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AgentAuditLogRepository(AskLucyDbContext dbContext) : IAgentAuditLogRepository
{
    public void Add(AgentAuditLog log) => dbContext.AgentAuditLogs.Add(log);

    public async Task<IReadOnlyList<AgentAuditLog>> ListByExecutionIdAsync(Guid agentExecutionId, CancellationToken cancellationToken = default) =>
        await dbContext.AgentAuditLogs
            .Where(a => a.AgentExecutionId == agentExecutionId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);
}
