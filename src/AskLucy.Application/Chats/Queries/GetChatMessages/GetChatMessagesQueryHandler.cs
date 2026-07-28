using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatMessages;

/// <summary>Owner-scoped (FR-018): a chat that doesn't exist or isn't the caller's own reports identically as not-found.</summary>
public sealed class GetChatMessagesQueryHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetChatMessagesQuery, IReadOnlyList<MessageDto>>
{
    public async Task<IReadOnlyList<MessageDto>> Handle(GetChatMessagesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var messages = await messageRepository.ListByChatIdAsync(request.ChatId, cancellationToken);

        return [.. messages.Select(m => new MessageDto(m.Id, m.Role.ToString(), m.Kind.ToString(), m.Content, m.SourceText, m.CreatedAtUtc))];
    }
}
