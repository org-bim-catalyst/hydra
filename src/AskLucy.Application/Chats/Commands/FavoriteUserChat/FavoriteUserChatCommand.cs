using MediatR;

namespace AskLucy.Application.Chats.Commands.FavoriteUserChat;

/// <summary>Marks a conversation as a favorite, independently of pin/archive state (FR-009).</summary>
public sealed record FavoriteUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
