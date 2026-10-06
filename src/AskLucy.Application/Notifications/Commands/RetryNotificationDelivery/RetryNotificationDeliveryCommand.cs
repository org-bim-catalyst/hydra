using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.RetryNotificationDelivery;

/// <summary>contracts/admin-notifications-api.md POST /notifications/deliveries/{deliveryId}/actions/retry (M).</summary>
public sealed record RetryNotificationDeliveryCommand(Guid DeliveryId) : IRequest<RetryNotificationDeliveryResult>;

public sealed record RetryNotificationDeliveryResult(Guid DeliveryId, DeliveryStatus Status);
