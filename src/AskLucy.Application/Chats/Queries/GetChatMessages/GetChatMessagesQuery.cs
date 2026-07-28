using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatMessages;

public sealed record GetChatMessagesQuery(Guid ChatId) : IRequest<IReadOnlyList<MessageDto>>;
