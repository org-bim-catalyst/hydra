using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

public enum VoiceProviderFailoverDirection
{
    FailedOverToFallback,
    RecoveredToPrimary,
}

/// <summary>
/// One voice-session failover/recovery event between the primary (ElevenLabs) and legacy
/// fallback voice implementations (FR-033/FR-034/FR-039). Append-only log — no soft delete,
/// no mutation methods, same convention as <see cref="ProviderHealthCheck"/>. Never carries
/// transcript/response text — only provider-health metadata (data-model.md).
/// </summary>
public sealed class VoiceProviderFailoverEvent : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public DateTime OccurredAtUtc { get; private set; }

    public VoiceProviderFailoverDirection Direction { get; private set; }

    /// <summary>A short, sanitized error summary — never the raw provider exception verbatim
    /// if it could contain the API key (constitution §14, same rule as
    /// <see cref="ProviderHealthCheck.Detail"/>).</summary>
    public string? Reason { get; private set; }

    private VoiceProviderFailoverEvent()
    {
        // Required by EF Core materialization.
    }

    public static VoiceProviderFailoverEvent Create(
        string userId, DateTime occurredAtUtc, VoiceProviderFailoverDirection direction, string? reason, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A voice provider failover event must belong to a user.");
        }

        return new VoiceProviderFailoverEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OccurredAtUtc = occurredAtUtc,
            Direction = direction,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
