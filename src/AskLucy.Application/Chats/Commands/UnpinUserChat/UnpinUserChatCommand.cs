using MediatR;

namespace AskLucy.Application.Chats.Commands.UnpinUserChat;

/// <summary>Unpins a conversation (FR-008).</summary>
public sealed record UnpinUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
