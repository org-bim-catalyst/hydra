using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Retrieval.Queries.KeywordSearch;

/// <summary>FR-018, FR-027 — ranks chunks by full-text relevance via <see cref="IKeywordSearchService"/>.</summary>
public sealed class KeywordSearchQueryHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKeywordSearchService keywordSearchService,
    SearchResultEnricher enricher,
    ICurrentUserAccessor currentUser) : IRequestHandler<KeywordSearchQuery, IReadOnlyList<SearchResultItemDto>>
{
    public async Task<IReadOnlyList<SearchResultItemDto>> Handle(KeywordSearchQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var scopedKnowledgeBaseIds = await knowledgeBaseRepository.ResolveOwnedIdsAsync(
            userId, request.KnowledgeBaseIds, request.ExcludeKnowledgeBaseIds, cancellationToken);

        if (scopedKnowledgeBaseIds.Count == 0)
        {
            return [];
        }

        var candidates = await keywordSearchService.SearchAsync(request.Query, scopedKnowledgeBaseIds, request.TopK, cancellationToken);

        var maxRank = candidates.Count > 0 ? candidates.Max(c => c.Rank) : 0;
        var ranked = candidates
            .Select(c => (
                c.DocumentChunkId,
                RelevanceScore: NormalizeRank(c.Rank, maxRank),
                SemanticScore: (decimal?)null,
                KeywordScore: (decimal?)NormalizeRank(c.Rank, maxRank),
                BoostFactors: (IReadOnlyDictionary<string, decimal>?)null))
            .ToList();

        return await enricher.EnrichAsync(ranked, cancellationToken);
    }

    /// <summary>CONTAINSTABLE's RANK is an unbounded-scale int (typically 0–1000, but not guaranteed) — normalized to a 0–1 relevance score relative to this result set's own maximum, so it's comparable in shape to the 0–1 cosine-similarity score.</summary>
    private static decimal NormalizeRank(double rank, double maxRank) => maxRank > 0 ? (decimal)(rank / maxRank) : 0m;
}
