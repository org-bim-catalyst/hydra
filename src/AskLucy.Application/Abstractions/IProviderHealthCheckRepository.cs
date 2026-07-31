using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Append-only log repository for <see cref="ProviderHealthCheck"/> (constitution &#167;3 Repository rules).</summary>
public interface IProviderHealthCheckRepository
{
    /// <summary>Most recent checks for a provider, newest first (FR-027, User Story 6).</summary>
    Task<IReadOnlyList<ProviderHealthCheck>> ListRecentByProviderIdAsync(Guid providerId, int take, CancellationToken cancellationToken = default);

    void Add(ProviderHealthCheck check);
}
