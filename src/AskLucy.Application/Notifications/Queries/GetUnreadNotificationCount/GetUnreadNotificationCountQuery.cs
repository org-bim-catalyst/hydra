using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetUnreadNotificationCount;

/// <summary>
/// contracts/notifications-api.md GET /notifications/unread-count — an index-only count (research
/// R19) the client polls only after a SignalR reconnect; otherwise it uses the pushed
/// <c>unreadCountChanged</c> events.
/// </summary>
public sealed record GetUnreadNotificationCountQuery : IRequest<int>;
