using MediatR;

namespace AskLucy.Application.Chats.Commands.UnfavoriteUserChat;

/// <summary>Removes a conversation's favorite designation (FR-009).</summary>
public sealed record UnfavoriteUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
