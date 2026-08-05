using MediatR;

namespace AskLucy.Application.Retrieval.Queries.HybridSearch;

/// <summary>FR-019, FR-020, FR-027, FR-029 — blends semantic and keyword relevance into a single ranked result set.</summary>
public sealed record HybridSearchQuery(
    string Query,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds,
    IReadOnlyCollection<Guid>? ExcludeKnowledgeBaseIds,
    int TopK,
    decimal SimilarityThreshold,
    decimal SemanticWeight = 0.5m) : IRequest<IReadOnlyList<SearchResultItemDto>>;
