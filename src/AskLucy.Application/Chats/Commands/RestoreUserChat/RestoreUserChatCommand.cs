using MediatR;

namespace AskLucy.Application.Chats.Commands.RestoreUserChat;

/// <summary>Restores a conversation from Archived or Recently Deleted back to the default view (FR-005a/FR-007), preserving prior pin/favorite state.</summary>
public sealed record RestoreUserChatCommand(Guid ChatId) : IRequest<UserChatSummaryDto>;
