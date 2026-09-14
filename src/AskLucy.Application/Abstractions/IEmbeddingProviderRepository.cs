using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="EmbeddingProvider"/> (constitution §3 Repository rules).</summary>
public interface IEmbeddingProviderRepository
{
    Task<EmbeddingProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The platform default provider for a hosting type (research.md Decision 5) — used when a knowledge base's <c>EmbeddingProviderId</c> is null.</summary>
    Task<EmbeddingProvider?> GetDefaultAsync(EmbeddingHostingType hostingType, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmbeddingProvider>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a provider to the current unit of work. The platform's own baseline rows are seeded by
    /// the <c>AddRetrievalEngine</c> migration, so this exists for the one case that seed cannot
    /// cover: restoring those rows when they have been deleted out from under an already-applied
    /// migration (<c>DevBaselineSeeder</c> — see its doc comment).
    /// </summary>
    void Add(EmbeddingProvider provider);
}
