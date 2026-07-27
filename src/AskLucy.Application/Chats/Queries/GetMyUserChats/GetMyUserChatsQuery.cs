using MediatR;

namespace AskLucy.Application.Chats.Queries.GetMyUserChats;

public sealed record GetMyUserChatsQuery : IRequest<IReadOnlyList<UserChatDto>>;
