using AskLucy.Application.Common;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotifications;

/// <summary>The notification center list (US1, contracts/notifications-api.md GET /notifications).</summary>
public sealed record GetNotificationsQuery(
    string? Cursor = null,
    int Limit = 25,
    IReadOnlyList<NotificationCategory>? Categories = null,
    NotificationReadState State = NotificationReadState.All) : IRequest<PagedResult<NotificationListItemDto>>;
