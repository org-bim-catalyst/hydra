using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Append-only log repository for <see cref="VoiceProviderFailoverEvent"/>
/// (constitution §3 Repository rules) — write side for FR-033/FR-034, read side for the
/// admin-facing <c>GetVoiceProviderHealthQuery</c> (FR-039/SC-011,
/// contracts/voice-provider-health.md).</summary>
public interface IVoiceProviderFailoverEventRepository
{
    void Add(VoiceProviderFailoverEvent failoverEvent);

    Task<IReadOnlyList<VoiceProviderFailoverEvent>> GetEventsAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The single most recent event for one user, if any — used to detect whether a
    /// now-succeeding call is a recovery from a prior failover (research.md Decision 5), so
    /// <c>RecoveredToPrimary</c> is only recorded (and <c>currentStatus</c> only flips back to
    /// healthy) when there actually was a preceding, still-unresolved failover.</summary>
    Task<VoiceProviderFailoverEvent?> GetMostRecentForUserAsync(string userId, CancellationToken cancellationToken = default);
}
