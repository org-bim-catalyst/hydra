using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatById;

/// <summary>
/// Owner-scoped, same as every other single-chat operation (FR-018, User Story 3) — a chat
/// that doesn't exist or doesn't belong to the caller is reported identically (404).
/// </summary>
public sealed class GetChatByIdQueryHandler(
    IUserChatRepository repository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetChatByIdQuery, ChatDetailDto>
{
    public async Task<ChatDetailDto> Handle(GetChatByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await repository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        return ChatDetailDto.FromEntity(chat);
    }
}
