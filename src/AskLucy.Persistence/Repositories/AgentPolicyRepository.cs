using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AgentPolicyRepository(AskLucyDbContext dbContext) : IAgentPolicyRepository
{
    public Task<AgentPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.AgentPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(AgentPolicy policy) => dbContext.AgentPolicies.Add(policy);

    public async Task<IReadOnlyList<AgentPolicy>> ListEnabledByToolNameAsync(string toolName, CancellationToken cancellationToken = default) =>
        await dbContext.AgentPolicies
            .Where(p => p.ToolName == toolName && p.IsEnabled)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentPolicy>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AgentPolicies.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<AgentUserExecutionLimit?> GetUserExecutionLimitAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.AgentUserExecutionLimits.FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

    public void AddUserExecutionLimit(AgentUserExecutionLimit limit) => dbContext.AgentUserExecutionLimits.Add(limit);
}
