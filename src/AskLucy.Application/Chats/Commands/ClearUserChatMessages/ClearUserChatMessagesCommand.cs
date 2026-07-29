using MediatR;

namespace AskLucy.Application.Chats.Commands.ClearUserChatMessages;

/// <summary>Clears every message from a conversation, keeping the conversation and its title (FR-011). Requires explicit confirmation.</summary>
public sealed record ClearUserChatMessagesCommand(Guid ChatId, bool Confirm) : IRequest;
