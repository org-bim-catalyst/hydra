using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Queries.SearchDocuments;

/// <summary>FR-035–FR-037's full filter set (US4, tasks.md T098) alongside the US1 view/folder scoping.</summary>
public sealed record SearchDocumentsQuery(
    DocumentListView View,
    Guid? FolderId,
    string? Cursor,
    int PageSize,
    string? Query = null,
    string? Author = null,
    string? LanguageCode = null,
    string? Tag = null,
    Guid? CategoryId = null,
    DateTime? DateFrom = null,
    DateTime? DateTo = null,
    DocumentProcessingStatus? Status = null) : IRequest<PagedResult<DocumentSummaryDto>>
{
    public DocumentSearchFilters ToFilters() => new(Query, Author, LanguageCode, Tag, CategoryId, DateFrom, DateTo, Status);
}
