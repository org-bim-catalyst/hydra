using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="AIModel"/> (constitution &#167;3 Repository rules).</summary>
public interface IAIModelRepository
{
    Task<AIModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every model for a provider, any status (admin view — FR-006).</summary>
    Task<IReadOnlyList<AIModel>> ListByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>Only <see cref="AIModelStatus.Available"/> models for a provider (user-facing catalog — FR-007).</summary>
    Task<IReadOnlyList<AIModel>> ListAvailableByProviderIdAsync(Guid providerId, CancellationToken cancellationToken = default);

    /// <summary>Every <see cref="AIModelStatus.Available"/> model across every enabled provider — the flat cross-provider catalog (contracts/providers.md).</summary>
    Task<IReadOnlyList<AIModel>> ListAvailableAsync(CancellationToken cancellationToken = default);

    void Add(AIModel model);
}
