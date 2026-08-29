using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// One health-check outcome for an <see cref="AIProvider"/> (FR-027). Append-only log — no
/// soft delete, no mutation methods (data-model.md).
/// </summary>
public sealed class ProviderHealthCheck : BaseEntity
{
    public Guid ProviderId { get; private set; }

    public DateTime CheckedAtUtc { get; private set; }

    public bool IsHealthy { get; private set; }

    /// <summary>Error summary when unhealthy — never the raw provider exception verbatim if it could contain the credential (constitution §14).</summary>
    public string? Detail { get; private set; }

    /// <summary>How this check failed (specs/043 FR-016). Non-null only when <see cref="IsHealthy"/> is false.</summary>
    public AiProviderFailureKind? FailureKind { get; private set; }

    /// <summary>Administrator-facing prose for <see cref="FailureKind"/>. Never the raw vendor response body or the credential.</summary>
    public string? FailureReason { get; private set; }

    private ProviderHealthCheck()
    {
        // Required by EF Core materialization.
    }

    public static ProviderHealthCheck Create(
        Guid providerId,
        DateTime checkedAtUtc,
        bool isHealthy,
        string? detail,
        string actor,
        AiProviderFailureKind? failureKind = null,
        string? failureReason = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            ProviderId = providerId,
            CheckedAtUtc = checkedAtUtc,
            IsHealthy = isHealthy,
            Detail = detail,
            FailureKind = isHealthy ? null : failureKind,
            FailureReason = isHealthy ? null : failureReason,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
}
