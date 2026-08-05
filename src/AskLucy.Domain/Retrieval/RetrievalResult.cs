using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>
/// A single ranked chunk returned by a retrieval, including its relevance score and ranking
/// factors (spec.md FR-026–FR-029, data-model.md). Append-only.
/// </summary>
public sealed class RetrievalResult : BaseEntity
{
    public Guid RetrievalHistoryId { get; private set; }

    public Guid DocumentChunkId { get; private set; }

    public int Rank { get; private set; }

    public decimal RelevanceScore { get; private set; }

    public decimal? SemanticScore { get; private set; }

    public decimal? KeywordScore { get; private set; }

    public string? BoostFactorsJson { get; private set; }

    private RetrievalResult()
    {
        // Required by EF Core materialization.
    }

    public static RetrievalResult Create(
        Guid retrievalHistoryId, Guid documentChunkId, int rank, decimal relevanceScore,
        decimal? semanticScore, decimal? keywordScore, string? boostFactorsJson, string actor)
    {
        return new RetrievalResult
        {
            Id = Guid.CreateVersion7(),
            RetrievalHistoryId = retrievalHistoryId,
            DocumentChunkId = documentChunkId,
            Rank = rank,
            RelevanceScore = relevanceScore,
            SemanticScore = semanticScore,
            KeywordScore = keywordScore,
            BoostFactorsJson = boostFactorsJson,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
