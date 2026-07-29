using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Chats.Commands.PurgeUserChat;

internal static partial class PurgeUserChatCommandHandlerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation {ChatId} permanently deleted by {UserId} (FR-028 audit event)")]
    public static partial void ConversationPurged(ILogger logger, Guid chatId, string userId);
}

/// <summary>
/// Owner-scoped (FR-026). Confirmation is enforced by <see cref="PurgeUserChatCommandValidator"/>
/// in the MediatR pipeline before this handler ever runs. Looks the conversation up bypassing
/// the soft-delete filter since it may already be in Recently Deleted (research.md Topic 2).
/// </summary>
public sealed class PurgeUserChatCommandHandler(
    IUserChatRepository repository,
    ICurrentUserAccessor currentUser,
    ILogger<PurgeUserChatCommandHandler> logger) : IRequestHandler<PurgeUserChatCommand>
{
    public async Task Handle(PurgeUserChatCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        ChatOwnershipGuard.EnsureOwnedBy(await repository.GetByIdIncludingDeletedAsync(request.ChatId, cancellationToken), userId);

        await repository.PurgeAsync(request.ChatId, cancellationToken);

        PurgeUserChatCommandHandlerLog.ConversationPurged(logger, request.ChatId, userId);
    }
}
