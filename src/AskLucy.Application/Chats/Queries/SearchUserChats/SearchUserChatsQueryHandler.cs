using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Chats.Queries.SearchUserChats;

/// <summary>Returns only the caller's own chats (FR-026) — search/filter/sort/paginated (FR-019–FR-024).</summary>
public sealed class SearchUserChatsQueryHandler(
    IUserChatRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<SearchUserChatsQuery, PagedResult<UserChatSummaryDto>>
{
    public async Task<PagedResult<UserChatSummaryDto>> Handle(SearchUserChatsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var (items, nextCursor) = await repository.SearchAsync(
            userId, request.View, request.PinnedOnly, request.FavoriteOnly, request.Query,
            request.Sort, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<UserChatSummaryDto>([.. items.Select(UserChatSummaryDto.FromEntity)], nextCursor);
    }
}
