using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>The raw aggregate values a <see cref="DocumentStatistics"/> row is refreshed from (US6, data-model.md).</summary>
public sealed record DocumentStatisticsAggregate(
    int TotalDocuments,
    long TotalStorageBytes,
    long? AverageProcessingDurationMs,
    string FileTypeDistributionJson,
    string LanguageDistributionJson);

/// <summary>Repository for <see cref="DocumentStatistics"/> (constitution §3 Repository rules) — used by both <c>DocumentStatisticsRecomputeJob</c> (writes) and the dashboard queries (reads).</summary>
public interface IDocumentStatisticsRepository
{
    Task<DocumentStatistics?> GetByScopeAsync(DocumentStatisticsScope scope, string? ownerId, CancellationToken cancellationToken = default);

    void Add(DocumentStatistics statistics);

    /// <summary>Every distinct owner with at least one non-deleted document — the per-user rows the recompute job must refresh.</summary>
    Task<IReadOnlyList<string>> ListDistinctOwnerIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>Computes the raw aggregate for one owner's documents (<paramref name="ownerId"/> non-null) or the whole organization (null).</summary>
    Task<DocumentStatisticsAggregate> ComputeAggregateAsync(string? ownerId, CancellationToken cancellationToken = default);
}
