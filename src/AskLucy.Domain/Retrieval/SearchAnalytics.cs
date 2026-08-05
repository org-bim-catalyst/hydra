using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// Periodically computed, denormalized search-activity metrics, optionally scoped to a knowledge
/// base (spec.md FR-042, FR-044, data-model.md).
/// </summary>
public sealed class SearchAnalytics : BaseEntity
{
    public Guid? KnowledgeBaseId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    public int SearchCount { get; private set; }

    public int? AverageRetrievalTimeMs { get; private set; }

    public decimal? AverageSimilarityScore { get; private set; }

    public int FailedSearchCount { get; private set; }

    public int EmptySearchCount { get; private set; }

    public string? TopDocumentsJson { get; private set; }

    public DateTime ComputedAtUtc { get; private set; }

    private SearchAnalytics()
    {
        // Required by EF Core materialization.
    }

    public static SearchAnalytics Create(
        Guid? knowledgeBaseId, string userId, int searchCount, int? averageRetrievalTimeMs,
        decimal? averageSimilarityScore, int failedSearchCount, int emptySearchCount,
        string? topDocumentsJson, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("Search analytics must belong to a user.");
        }

        var computedAtUtc = DateTime.UtcNow;

        return new SearchAnalytics
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            UserId = userId,
            SearchCount = searchCount,
            AverageRetrievalTimeMs = averageRetrievalTimeMs,
            AverageSimilarityScore = averageSimilarityScore,
            FailedSearchCount = failedSearchCount,
            EmptySearchCount = emptySearchCount,
            TopDocumentsJson = topDocumentsJson,
            ComputedAtUtc = computedAtUtc,
            CreatedAtUtc = computedAtUtc,
            CreatedBy = actor,
        };
    }

    public void Refresh(
        int searchCount, int? averageRetrievalTimeMs, decimal? averageSimilarityScore,
        int failedSearchCount, int emptySearchCount, string? topDocumentsJson, string actor)
    {
        SearchCount = searchCount;
        AverageRetrievalTimeMs = averageRetrievalTimeMs;
        AverageSimilarityScore = averageSimilarityScore;
        FailedSearchCount = failedSearchCount;
        EmptySearchCount = emptySearchCount;
        TopDocumentsJson = topDocumentsJson;
        ComputedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
