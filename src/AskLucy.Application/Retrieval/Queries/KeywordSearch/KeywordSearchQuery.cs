using MediatR;

namespace AskLucy.Application.Retrieval.Queries.KeywordSearch;

/// <summary>FR-018, FR-021, FR-027 — literal keyword search.</summary>
public sealed record KeywordSearchQuery(
    string Query,
    IReadOnlyCollection<Guid>? KnowledgeBaseIds,
    IReadOnlyCollection<Guid>? ExcludeKnowledgeBaseIds,
    int TopK) : IRequest<IReadOnlyList<SearchResultItemDto>>;
