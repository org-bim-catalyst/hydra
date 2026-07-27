using MediatR;

namespace AskLucy.Application.Chats.Commands.DeleteUserChat;

public sealed record DeleteUserChatCommand(Guid ChatId) : IRequest;
