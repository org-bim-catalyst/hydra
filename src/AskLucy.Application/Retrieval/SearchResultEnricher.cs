using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Retrieval;

/// <summary>
/// Turns a ranked list of (chunkId, scores) into full <see cref="SearchResultItemDto"/>s —
/// shared by <c>SemanticSearchQueryHandler</c>/<c>KeywordSearchQueryHandler</c>/
/// <c>HybridSearchQueryHandler</c> so the chunk/knowledge-base/document lookups aren't
/// triplicated (constitution §2.III DRY).
/// </summary>
public sealed class SearchResultEnricher(
    IDocumentChunkRepository documentChunkRepository,
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseDocumentRepository knowledgeBaseDocumentRepository)
{
    public async Task<IReadOnlyList<SearchResultItemDto>> EnrichAsync(
        IReadOnlyList<(Guid ChunkId, decimal RelevanceScore, decimal? SemanticScore, decimal? KeywordScore, IReadOnlyDictionary<string, decimal>? BoostFactors)> ranked,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResultItemDto>();
        var knowledgeBaseNameCache = new Dictionary<Guid, string>();
        var documentTitleCache = new Dictionary<Guid, string>();
        var rank = 1;

        foreach (var item in ranked)
        {
            var chunk = await documentChunkRepository.GetByIdAsync(item.ChunkId, cancellationToken);
            if (chunk is null)
            {
                continue;
            }

            if (!knowledgeBaseNameCache.TryGetValue(chunk.KnowledgeBaseId, out var knowledgeBaseName))
            {
                var knowledgeBase = await knowledgeBaseRepository.GetByIdAsync(chunk.KnowledgeBaseId, cancellationToken);
                knowledgeBaseName = knowledgeBase?.Name ?? "Unknown";
                knowledgeBaseNameCache[chunk.KnowledgeBaseId] = knowledgeBaseName;
            }

            if (!documentTitleCache.TryGetValue(chunk.KnowledgeBaseDocumentId, out var documentTitle))
            {
                var knowledgeBaseDocument = await knowledgeBaseDocumentRepository.GetByIdAsync(chunk.KnowledgeBaseDocumentId, cancellationToken);
                documentTitle = knowledgeBaseDocument?.FileName ?? "Unknown";
                documentTitleCache[chunk.KnowledgeBaseDocumentId] = documentTitle;
            }

            const int excerptLength = 280;
            var excerpt = chunk.Content.Length > excerptLength ? chunk.Content[..excerptLength] + "…" : chunk.Content;

            results.Add(new SearchResultItemDto(
                chunk.Id, chunk.DocumentId, chunk.DocumentVersionId, chunk.KnowledgeBaseId,
                documentTitle, knowledgeBaseName, chunk.PageNumber, chunk.Section, excerpt,
                item.RelevanceScore, item.SemanticScore, item.KeywordScore, item.BoostFactors, rank++));
        }

        return results;
    }
}
