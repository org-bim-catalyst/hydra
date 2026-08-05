using AskLucy.Application.Retrieval.Queries.KeywordSearch;
using AskLucy.Application.Retrieval.Queries.SemanticSearch;
using MediatR;

namespace AskLucy.Application.Retrieval.Queries.HybridSearch;

/// <summary>
/// FR-019, FR-027 — reuses <see cref="SemanticSearchQuery"/> and <see cref="KeywordSearchQuery"/>
/// (constitution §2.III DRY — the ranking math lives once, in application code, independent of
/// the database, per research.md Decision 6) rather than re-implementing candidate retrieval a
/// third time, then blends the two result sets by chunk id. Simplification (documented for
/// follow-up refinement): each side is queried for the same <see cref="HybridSearchQuery.TopK"/>
/// candidates rather than a wider pool, so a chunk ranked outside both individual top-K lists can
/// never surface here even if its blended score would qualify.
/// </summary>
public sealed class HybridSearchQueryHandler(IMediator mediator) : IRequestHandler<HybridSearchQuery, IReadOnlyList<SearchResultItemDto>>
{
    public async Task<IReadOnlyList<SearchResultItemDto>> Handle(HybridSearchQuery request, CancellationToken cancellationToken)
    {
        var semanticResults = await mediator.Send(
            new SemanticSearchQuery(request.Query, request.KnowledgeBaseIds, request.ExcludeKnowledgeBaseIds, request.TopK, request.SimilarityThreshold),
            cancellationToken);

        var keywordResults = await mediator.Send(
            new KeywordSearchQuery(request.Query, request.KnowledgeBaseIds, request.ExcludeKnowledgeBaseIds, request.TopK),
            cancellationToken);

        var byChunkId = new Dictionary<Guid, SearchResultItemDto>();

        foreach (var result in semanticResults)
        {
            byChunkId[result.ChunkId] = result;
        }

        foreach (var result in keywordResults)
        {
            byChunkId[result.ChunkId] = byChunkId.TryGetValue(result.ChunkId, out var existing)
                ? existing with { KeywordScore = result.KeywordScore }
                : result;
        }

        var semanticWeight = request.SemanticWeight;
        var keywordWeight = 1m - semanticWeight;

        var blended = byChunkId.Values
            .Select(r => r with { RelevanceScore = (r.SemanticScore ?? 0m) * semanticWeight + (r.KeywordScore ?? 0m) * keywordWeight })
            .OrderByDescending(r => r.RelevanceScore)
            .Take(request.TopK)
            .Select((r, index) => r with { Rank = index + 1 })
            .ToList();

        return blended;
    }
}
