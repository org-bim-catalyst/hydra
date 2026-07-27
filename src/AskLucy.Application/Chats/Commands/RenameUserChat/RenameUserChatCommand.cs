using MediatR;

namespace AskLucy.Application.Chats.Commands.RenameUserChat;

/// <summary>FR-033 — approved, minor completion of the chat entity's lifecycle.</summary>
public sealed record RenameUserChatCommand(Guid ChatId, string NewTitle) : IRequest<UserChatDto>;
