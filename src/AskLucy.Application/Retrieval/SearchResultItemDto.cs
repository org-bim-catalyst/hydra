namespace AskLucy.Application.Retrieval;

/// <summary>One ranked search result (contracts/retrieval-search-api.md), with the ranking factors that produced it (spec.md FR-029, US4).</summary>
public sealed record SearchResultItemDto(
    Guid ChunkId,
    Guid DocumentId,
    Guid DocumentVersionId,
    Guid KnowledgeBaseId,
    string DocumentTitle,
    string KnowledgeBaseName,
    int? PageNumber,
    string? Section,
    string Excerpt,
    decimal RelevanceScore,
    decimal? SemanticScore,
    decimal? KeywordScore,
    IReadOnlyDictionary<string, decimal>? BoostFactors,
    int Rank);
