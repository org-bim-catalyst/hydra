using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="Workflow"/> (constitution §3 Repository rules).</summary>
public interface IWorkflowRepository
{
    Task<Workflow?> GetByIdForOwnerAsync(Guid id, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Bypasses owner scoping — used only where the caller has already authorized access some other way (e.g. the Workflow Runtime resolving the workflow an execution belongs to, not the HTTP caller directly).</summary>
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Workflow workflow);

    /// <summary>FR-001 — case-insensitive per-owner uniqueness check; SQL Server's default collation already performs case-insensitive comparison.</summary>
    Task<bool> ExistsWithNameForOwnerAsync(string ownerId, string name, Guid? excludingWorkflowId, CancellationToken cancellationToken = default);

    /// <summary>Cursor-based (keyset) listing over the caller's own workflows (contracts/workflows-api.md <c>ListWorkflowsQuery</c>).</summary>
    Task<(IReadOnlyList<Workflow> Items, string? NextCursor)> ListByOwnerAsync(
        string ownerId, WorkflowStatus? status, WorkflowType? workflowType, string? search, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    Task<WorkflowVersion?> GetVersionAsync(Guid workflowId, int versionNumber, CancellationToken cancellationToken = default);

    /// <summary>Looked up by the version's own id — used where a caller (e.g. <c>WorkflowExecution.WorkflowVersionId</c>) only has that, not the version number.</summary>
    Task<WorkflowVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowVersion>> ListVersionsAsync(Guid workflowId, CancellationToken cancellationToken = default);

    /// <summary>Every published, enabled Event-Driven workflow's currently-published version — the event-trigger dispatch path's candidate set (FR-064, research.md Decision 12).</summary>
    Task<IReadOnlyList<(Workflow Workflow, WorkflowVersion Version)>> ListPublishedEventDrivenAsync(CancellationToken cancellationToken = default);
}
