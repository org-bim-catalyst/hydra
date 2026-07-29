using MediatR;

namespace AskLucy.Application.Chats.Commands.ArchiveUserChat;

/// <summary>Archives a conversation (FR-006), removing it from the default view without deleting it.</summary>
public sealed record ArchiveUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
