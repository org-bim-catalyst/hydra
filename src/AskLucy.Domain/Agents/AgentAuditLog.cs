using AskLucy.Domain.Common;

namespace AskLucy.Domain.Agents;

public enum AgentAuditAction
{
    PermissionChecked,
    PermissionDenied,
    ApprovalDecided,
    CrossUserAccessAttempted,
    ExecutionCompleted,
    ExecutionFailed,
}

/// <summary>
/// Tamper-resistant security record (spec.md FR-050, data-model.md) — append-only, deliberately
/// not hard-FK'd to <see cref="AgentExecution"/> so an entry for a later-purged execution is
/// retained (mirrors <c>KnowledgeBaseAuditLogs</c>).
/// </summary>
public sealed class AgentAuditLog : BaseEntity
{
    public Guid AgentExecutionId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public AgentAuditAction Action { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public DateTime OccurredAtUtc { get; private set; }

    private AgentAuditLog()
    {
        // Required by EF Core materialization.
    }

    public static AgentAuditLog Create(Guid agentExecutionId, string userId, AgentAuditAction action, string detailsJson) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentExecutionId = agentExecutionId,
        UserId = userId,
        Action = action,
        DetailsJson = detailsJson,
        OccurredAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow,
        CreatedBy = "system",
    };
}
