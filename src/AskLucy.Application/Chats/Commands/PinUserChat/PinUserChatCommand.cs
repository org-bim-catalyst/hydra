using MediatR;

namespace AskLucy.Application.Chats.Commands.PinUserChat;

/// <summary>Pins a conversation so it sorts ahead of unpinned ones (FR-008).</summary>
public sealed record PinUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
