using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using AskLucy.Application.Chats.Commands.AppendMessage;
using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatMessages;

/// <summary>Owner-scoped (FR-018): a chat that doesn't exist or isn't the caller's own reports identically as not-found.</summary>
public sealed class GetChatMessagesQueryHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetChatMessagesQuery, PagedResult<MessageDto>>
{
    public async Task<PagedResult<MessageDto>> Handle(GetChatMessagesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var (messages, nextCursor) = await messageRepository.ListPagedByChatIdAsync(
            request.ChatId, request.Cursor, request.PageSize, cancellationToken);

        return new PagedResult<MessageDto>([.. messages.Select(AppendMessageCommandHandler.ToDto)], nextCursor);
    }
}
