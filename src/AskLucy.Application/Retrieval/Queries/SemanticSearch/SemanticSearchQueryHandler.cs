using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using MediatR;

namespace AskLucy.Application.Retrieval.Queries.SemanticSearch;

/// <summary>
/// FR-017, FR-026 — embeds the query text and ranks chunks by cosine similarity via each resolved
/// knowledge base's <see cref="IVectorStore"/> (ADR-0007). Embedding-provider simplification
/// (documented for follow-up refinement): the query is embedded once, using the first resolved
/// knowledge base's configured embedding provider — a multi-knowledge-base search where the bases
/// use genuinely different embedding providers is not yet blended per-provider. Vector-store
/// selection does NOT share this simplification: knowledge bases spanning more than one
/// <see cref="VectorStoreProvider"/> are partitioned and queried against each store separately,
/// then merged — silently searching only the first knowledge base's store would drop other
/// knowledge bases' results with no error, which this project's "no silent failures" rule forbids.
/// </summary>
public sealed class SemanticSearchQueryHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IEmbeddingProviderRepository embeddingProviderRepository,
    IEmbeddingServiceResolver embeddingServiceResolver,
    IVectorStoreResolver vectorStoreResolver,
    SearchResultEnricher enricher,
    ICurrentUserAccessor currentUser) : IRequestHandler<SemanticSearchQuery, IReadOnlyList<SearchResultItemDto>>
{
    public async Task<IReadOnlyList<SearchResultItemDto>> Handle(SemanticSearchQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var scopedKnowledgeBaseIds = await knowledgeBaseRepository.ResolveOwnedIdsAsync(
            userId, request.KnowledgeBaseIds, request.ExcludeKnowledgeBaseIds, cancellationToken);

        if (scopedKnowledgeBaseIds.Count == 0)
        {
            return [];
        }

        var embeddingService = await ResolveEmbeddingServiceAsync(scopedKnowledgeBaseIds[0], cancellationToken);
        var queryEmbedding = await embeddingService.EmbedAsync(request.Query, cancellationToken);

        var scopedKnowledgeBases = await knowledgeBaseRepository.GetByIdsAsync(scopedKnowledgeBaseIds, cancellationToken);

        var knowledgeBaseIdsByProvider = scopedKnowledgeBases
            .GroupBy(kb => kb.VectorStoreProvider)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(kb => kb.Id).ToList());

        var candidateBatches = await Task.WhenAll(knowledgeBaseIdsByProvider.Select(kvp =>
            vectorStoreResolver.Resolve(kvp.Key).QueryNearestAsync(
                queryEmbedding.Vector, kvp.Value, request.TopK, (double)request.SimilarityThreshold, cancellationToken)));

        var candidates = candidateBatches.SelectMany(c => c).OrderBy(c => c.Distance).Take(request.TopK).ToList();

        var ranked = candidates
            .Select(c => (c.DocumentChunkId, RelevanceScore: DistanceToSimilarity(c.Distance), SemanticScore: (decimal?)DistanceToSimilarity(c.Distance), KeywordScore: (decimal?)null, BoostFactors: (IReadOnlyDictionary<string, decimal>?)null))
            .ToList();

        return await enricher.EnrichAsync(ranked, cancellationToken);
    }

    private async Task<IEmbeddingService> ResolveEmbeddingServiceAsync(Guid knowledgeBaseId, CancellationToken cancellationToken)
    {
        var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(knowledgeBaseId, cancellationToken);
        var providerId = knowledgeBase?.EmbeddingProviderId;

        var provider = providerId is not null
            ? await embeddingProviderRepository.GetByIdAsync(providerId.Value, cancellationToken)
            : await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken);

        provider ??= await embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, cancellationToken)
            ?? throw new InvalidOperationException("No default embedding provider is configured.");

        return embeddingServiceResolver.Resolve(provider.Vendor);
    }

    private static decimal DistanceToSimilarity(double cosineDistance) => (decimal)Math.Clamp(1.0 - cosineDistance, 0.0, 1.0);
}
