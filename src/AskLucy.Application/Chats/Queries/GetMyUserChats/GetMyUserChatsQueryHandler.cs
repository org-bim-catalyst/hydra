using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Chats.Queries.GetMyUserChats;

/// <summary>
/// Returns only the caller's own chats — closes the legacy cross-user enumeration gap
/// (FR-018, User Story 3).
/// </summary>
public sealed class GetMyUserChatsQueryHandler(
    IUserChatRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetMyUserChatsQuery, IReadOnlyList<UserChatDto>>
{
    public async Task<IReadOnlyList<UserChatDto>> Handle(GetMyUserChatsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chats = await repository.ListByUserIdAsync(userId, cancellationToken);

        return [.. chats.Select(c => new UserChatDto(c.Id, c.Title, c.CreatedAtUtc, c.ModifiedAtUtc))];
    }
}
