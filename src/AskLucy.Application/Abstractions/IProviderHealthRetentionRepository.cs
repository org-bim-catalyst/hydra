using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// The singleton provider health-check retention setting and its append-only audit trail (specs/082). The row is seeded
/// by the migration; there is no create path.
/// </summary>
public interface IProviderHealthRetentionRepository
{
    /// <summary>The singleton row, tracked so the caller can change it; null when the seed row is missing.</summary>
    Task<ProviderHealthRetentionSetting?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Makes the next save fail with a concurrency conflict unless the row still has <paramref name="rowVersion"/>.</summary>
    void ExpectRowVersion(ProviderHealthRetentionSetting setting, byte[] rowVersion);

    /// <summary>Adds an append-only audit row to the current unit of work; the caller's save commits it.</summary>
    void AddAudit(ProviderHealthRetentionAuditLog entry);
}
