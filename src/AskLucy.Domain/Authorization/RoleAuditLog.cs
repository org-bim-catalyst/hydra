using AskLucy.Domain.Common;

namespace AskLucy.Domain.Authorization;

/// <summary>
/// Append-only audit log entity for role-management events (research.md Decision 9).
/// Mirrors the <c>McpAuditLog</c> pattern — no setters beyond construction, no update/delete paths.
/// Not FK'd to a role/user — an entry for a later-deleted role or removed user is retained.
/// </summary>
public sealed class RoleAuditLog : BaseEntity, ICorrelated
{
    public RoleAuditAction Action { get; private set; }

    public string ActorUserId { get; private set; } = string.Empty;

    public string? TargetRoleId { get; private set; }

    public string? TargetRoleName { get; private set; }

    public string? TargetUserId { get; private set; }

    public string DetailsJson { get; private set; } = "{}";

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Set by the SaveChanges audit interceptor on insert (specs/074 FR-006b).</summary>
    public string? CorrelationId { get; private set; }

    private RoleAuditLog()
    {
        // Required by EF Core materialization.
    }

    /// <summary>
    /// Creates an audit log entry. Use this factory method exclusively.
    /// </summary>
    public static RoleAuditLog Record(
        RoleAuditAction action,
        string actorUserId,
        string? targetRoleId = null,
        string? targetRoleName = null,
        string? targetUserId = null,
        string? detailsJson = null)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user ID must be provided.", nameof(actorUserId));
        }

        var now = DateTime.UtcNow;
        return new RoleAuditLog
        {
            Id = Guid.CreateVersion7(),
            Action = action,
            ActorUserId = actorUserId.Trim(),
            TargetRoleId = targetRoleId?.Trim(),
            TargetRoleName = targetRoleName?.Trim(),
            TargetUserId = targetUserId?.Trim(),
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "{}" : detailsJson,
            OccurredAtUtc = now,
            CreatedAtUtc = now,
            CreatedBy = actorUserId.Trim(),
        };
    }
}
