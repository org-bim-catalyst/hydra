using AskLucy.Application.Abstractions;
using AskLucy.Domain.Workflows;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class WorkflowAuditLogRepository(AskLucyDbContext dbContext) : IWorkflowAuditLogRepository
{
    public void Add(WorkflowAuditLog log) => dbContext.WorkflowAuditLogs.Add(log);

    public async Task<IReadOnlyList<WorkflowAuditLog>> ListByWorkflowIdAsync(Guid workflowId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowAuditLogs
            .Where(a => a.WorkflowId == workflowId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkflowAuditLog>> ListByExecutionIdAsync(Guid workflowExecutionId, CancellationToken cancellationToken = default) =>
        await dbContext.WorkflowAuditLogs
            .Where(a => a.WorkflowExecutionId == workflowExecutionId)
            .OrderByDescending(a => a.OccurredAtUtc)
            .ToListAsync(cancellationToken);
}
