using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/082 — append-only audit of provider health-check retention changes. Mirrors the per-area audit logs
/// (<c>RoleAuditLog</c>): no setters beyond construction, no update or delete path, and no FK to the actor so an entry
/// outlives a removed user.
/// </summary>
public sealed class ProviderHealthRetentionAuditLog : BaseEntity, ICorrelated
{
    public const int ActorUserIdMaxLength = 450;

    public string ActorUserId { get; private set; } = string.Empty;

    public int PreviousRetentionDays { get; private set; }

    public int NewRetentionDays { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Set by the SaveChanges audit interceptor on insert.</summary>
    public string? CorrelationId { get; private set; }

    private ProviderHealthRetentionAuditLog()
    {
        // Required by EF Core materialization.
    }

    /// <summary>Creates an audit entry. Use this factory exclusively.</summary>
    public static ProviderHealthRetentionAuditLog Record(
        string actorUserId,
        int previousRetentionDays,
        int newRetentionDays,
        DateTime occurredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("Actor user ID must be provided.", nameof(actorUserId));
        }

        var actor = actorUserId.Trim();
        return new ProviderHealthRetentionAuditLog
        {
            Id = Guid.CreateVersion7(),
            ActorUserId = actor,
            PreviousRetentionDays = previousRetentionDays,
            NewRetentionDays = newRetentionDays,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = occurredAtUtc,
            CreatedBy = actor,
        };
    }
}
