using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Chats.Queries.GetChatMessages;

/// <summary>Cursor-paginated (FR-022/FR-024) so a very long conversation's history loads incrementally, oldest-first.</summary>
public sealed record GetChatMessagesQuery(Guid ChatId, string? Cursor = null, int PageSize = 50) : IRequest<PagedResult<MessageDto>>;
