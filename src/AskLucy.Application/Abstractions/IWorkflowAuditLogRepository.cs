using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="WorkflowAuditLog"/> (constitution §3 Repository rules) — deliberately its own top-level repository, not reachable via <see cref="IWorkflowExecutionRepository"/>, since it is intentionally not hard-linked to the workflow/execution it describes (data-model.md).</summary>
public interface IWorkflowAuditLogRepository
{
    void Add(WorkflowAuditLog log);

    Task<IReadOnlyList<WorkflowAuditLog>> ListByWorkflowIdAsync(Guid workflowId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowAuditLog>> ListByExecutionIdAsync(Guid workflowExecutionId, CancellationToken cancellationToken = default);
}
