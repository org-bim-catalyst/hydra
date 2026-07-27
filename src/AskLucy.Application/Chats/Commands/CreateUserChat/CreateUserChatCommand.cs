using MediatR;

namespace AskLucy.Application.Chats.Commands.CreateUserChat;

public sealed record CreateUserChatCommand(string Title, string? SessionId) : IRequest<UserChatDto>;
