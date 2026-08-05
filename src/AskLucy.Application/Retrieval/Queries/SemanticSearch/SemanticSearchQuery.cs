using AskLucy.Application.Retrieval;
using MediatR;

namespace AskLucy.Application.Retrieval.Queries.SemanticSearch;

/// <summary>FR-017, FR-021, FR-023, FR-024, FR-026 — semantic (cosine-similarity) search.</summary>
public sealed record SemanticSearchQuery(
    string Query,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds,
    IReadOnlyCollection<Guid>? ExcludeKnowledgeBaseIds,
    int TopK,
    decimal SimilarityThreshold) : IRequest<IReadOnlyList<SearchResultItemDto>>;
