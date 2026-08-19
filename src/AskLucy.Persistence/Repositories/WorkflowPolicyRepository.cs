using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class WorkflowPolicyRepository(AskLucyDbContext dbContext) : IWorkflowPolicyRepository
{
    public Task<WorkflowPolicy?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowPolicies.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(WorkflowPolicy policy) => dbContext.WorkflowPolicies.Add(policy);

    public async Task<IReadOnlyList<WorkflowPolicy>> ListEnabledForNodeAsync(WorkflowNodeType nodeType, string? underlyingToolName, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowPolicies
            .Where(p => p.IsEnabled && (p.WorkflowNodeType == nodeType || (underlyingToolName != null && p.UnderlyingToolName == underlyingToolName)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowPolicy>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowPolicies.OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public Task<WorkflowUserExecutionLimit?> GetUserExecutionLimitAsync(string userId, CancellationToken cancellationToken = default) =>
        dbContext.WorkflowUserExecutionLimits.FirstOrDefaultAsync(l => l.UserId == userId, cancellationToken);

    public void AddUserExecutionLimit(WorkflowUserExecutionLimit limit) => dbContext.WorkflowUserExecutionLimits.Add(limit);
}
