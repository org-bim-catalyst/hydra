using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.SearchKnowledgeBases;

/// <summary>
/// Searches/filters/sorts/paginates the caller's own knowledge bases (FR-022–FR-024,
/// `GET /api/v1/knowledge-bases`). US1 only used <see cref="View"/>; this is the full shape
/// US4 extends it to — the response shape (`PagedResult&lt;T&gt;`) never changed between the
/// two, only what the query is capable of returning.
/// </summary>
public sealed record SearchKnowledgeBasesQuery(
    KnowledgeBaseListView View = KnowledgeBaseListView.Active,
    string? Query = null,
    Guid? CategoryId = null,
    string? Tag = null,
    bool? FavoriteOnly = null,
    bool? PinnedOnly = null,
    KnowledgeBaseSort Sort = KnowledgeBaseSort.RecentlyUpdated,
    bool SortDescending = true,
    string? Cursor = null,
    int PageSize = 50) : IRequest<PagedResult<KnowledgeBaseSummaryDto>>;
