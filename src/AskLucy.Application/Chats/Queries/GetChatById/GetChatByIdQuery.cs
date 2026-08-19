using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatById;

/// <summary>contracts/chat-detail-api.md `GET /api/v1/chats/{id}` — fetches one chat's own detail, scoped to the caller.</summary>
public sealed record GetChatByIdQuery(Guid ChatId) : IRequest<ChatDetailDto>;
