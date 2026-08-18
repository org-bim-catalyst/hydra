using AskLucy.Application.Common;
using MediatR;

namespace AskLucy.Application.Memory.Queries.ListMemoryNotifications;

/// <summary>contracts/memory-privacy-api.md — `GET /api/v1/memories/notifications` (FR-006a, research.md Decision 11).</summary>
public sealed record MemoryNotificationDto(Guid Id, Guid? MemoryId, string EventType, string Message, DateTime CreatedAtUtc, DateTime? ReadAtUtc);

public sealed record ListMemoryNotificationsQuery(string? Cursor, int PageSize = 20) : IRequest<PagedResult<MemoryNotificationDto>>;
