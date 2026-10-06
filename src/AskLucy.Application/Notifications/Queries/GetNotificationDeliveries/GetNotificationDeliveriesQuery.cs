using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationDeliveries;

/// <summary>contracts/admin-notifications-api.md GET /notifications/deliveries. No status means failed and dead-lettered.</summary>
public sealed record GetNotificationDeliveriesQuery(
    IReadOnlyList<DeliveryStatus>? Statuses = null,
    NotificationChannel? Channel = null,
    NotificationCategory? Category = null,
    string? Type = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Cursor = null,
    int Limit = 50)
    : IRequest<AdminPage<AdminDeliveryDto>>, IAuditedAdminView
{
    public const int MaxLimit = 200;

    public NotificationAuditAction AuditAction => NotificationAuditAction.DeliveriesViewed;

    public string AuditTargetType => "NotificationDeliveries";

    public string AuditTargetId => "all";

    public AdminDeliveryFilter ToFilter() => new(
        Statuses is { Count: > 0 } ? Statuses : [DeliveryStatus.Failed, DeliveryStatus.DeadLettered],
        Channel, Category, Type, FromUtc, ToUtc);
}
