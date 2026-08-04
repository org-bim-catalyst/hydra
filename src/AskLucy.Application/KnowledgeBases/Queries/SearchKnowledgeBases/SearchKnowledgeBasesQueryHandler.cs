using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Queries.SearchKnowledgeBases;

public sealed class SearchKnowledgeBasesQueryHandler(
    IKnowledgeBaseRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<SearchKnowledgeBasesQuery, PagedResult<KnowledgeBaseSummaryDto>>
{
    public async Task<PagedResult<KnowledgeBaseSummaryDto>> Handle(SearchKnowledgeBasesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var (items, nextCursor) = await repository.SearchAsync(
            userId, request.View, request.Query, request.CategoryId, request.Tag, request.FavoriteOnly, request.PinnedOnly,
            request.Sort, request.SortDescending, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<KnowledgeBaseSummaryDto>([.. items.Select(KnowledgeBaseSummaryDto.FromEntity)], nextCursor);
    }
}
