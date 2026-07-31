using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="AIProvider"/> (constitution &#167;3 Repository rules).</summary>
public interface IAIProviderRepository
{
    Task<AIProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AIProvider?> GetByKeyAsync(string providerKey, CancellationToken cancellationToken = default);

    /// <summary>Every provider, enabled or not (admin view — FR-003).</summary>
    Task<IReadOnlyList<AIProvider>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Only enabled providers (user-facing catalog — FR-007).</summary>
    Task<IReadOnlyList<AIProvider>> ListEnabledAsync(CancellationToken cancellationToken = default);

    void Add(AIProvider provider);
}
