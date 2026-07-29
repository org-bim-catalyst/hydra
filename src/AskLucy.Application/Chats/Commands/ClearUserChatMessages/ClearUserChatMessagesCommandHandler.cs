using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Chats.Commands.ClearUserChatMessages;

internal static partial class ClearUserChatMessagesCommandHandlerLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "All messages cleared from conversation {ChatId} by {UserId}")]
    public static partial void MessagesCleared(ILogger logger, Guid chatId, string userId);
}

/// <summary>Owner-scoped (FR-026). Confirmation is enforced by <see cref="ClearUserChatMessagesCommandValidator"/> in the MediatR pipeline before this handler ever runs.</summary>
public sealed class ClearUserChatMessagesCommandHandler(
    IUserChatRepository chatRepository,
    IMessageRepository messageRepository,
    ICurrentUserAccessor currentUser,
    ILogger<ClearUserChatMessagesCommandHandler> logger) : IRequestHandler<ClearUserChatMessagesCommand>
{
    public async Task Handle(ClearUserChatMessagesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        await messageRepository.DeleteAllByChatIdAsync(request.ChatId, cancellationToken);

        ClearUserChatMessagesCommandHandlerLog.MessagesCleared(logger, request.ChatId, userId);
    }
}
