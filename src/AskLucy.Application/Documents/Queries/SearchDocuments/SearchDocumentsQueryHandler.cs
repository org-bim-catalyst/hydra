using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Documents.Queries.SearchDocuments;

public sealed class SearchDocumentsQueryHandler(
    IDocumentRepository documentRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<SearchDocumentsQuery, PagedResult<DocumentSummaryDto>>
{
    public async Task<PagedResult<DocumentSummaryDto>> Handle(SearchDocumentsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await documentRepository.SearchAsync(
            userId, request.View, request.FolderId, request.ToFilters(), request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<DocumentSummaryDto>(items.Select(DocumentSummaryDto.FromEntity).ToList(), nextCursor);
    }
}
