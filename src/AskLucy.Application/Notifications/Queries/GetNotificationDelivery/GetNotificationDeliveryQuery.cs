using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Queries.GetNotificationDelivery;

/// <summary>contracts/admin-notifications-api.md GET /notifications/deliveries/{deliveryId}. 404 when there is no such delivery.</summary>
public sealed record GetNotificationDeliveryQuery(Guid DeliveryId) : IRequest<AdminDeliveryDetailDto>, IAuditedAdminView
{
    public NotificationAuditAction AuditAction => NotificationAuditAction.DeliveryViewed;

    public string AuditTargetType => "NotificationDelivery";

    public string AuditTargetId => DeliveryId.ToString();
}
