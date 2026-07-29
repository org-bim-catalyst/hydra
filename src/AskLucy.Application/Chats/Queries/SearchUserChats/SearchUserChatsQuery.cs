using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Chats.Queries.SearchUserChats;

/// <summary>
/// Searches/filters/sorts/paginates the caller's own conversations (FR-019–FR-024).
/// Replaces the earlier <c>GetMyUserChatsQuery</c> (SPEC-000), which had none of these
/// parameters.
/// </summary>
public sealed record SearchUserChatsQuery(
    ConversationView View = ConversationView.Active,
    bool? PinnedOnly = null,
    bool? FavoriteOnly = null,
    string? Query = null,
    ConversationSort Sort = ConversationSort.Newest,
    string? Cursor = null,
    int PageSize = 50) : IRequest<PagedResult<UserChatSummaryDto>>;
