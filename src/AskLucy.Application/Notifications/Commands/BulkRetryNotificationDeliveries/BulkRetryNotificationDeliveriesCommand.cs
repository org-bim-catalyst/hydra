using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.BulkRetryNotificationDeliveries;

/// <summary>
/// contracts/admin-notifications-api.md POST /notifications/deliveries/actions/retry (M). Exactly one of
/// <paramref name="DeliveryIds"/> (1-200) or <paramref name="Filter"/> (at most 1,000 matches).
/// </summary>
public sealed record BulkRetryNotificationDeliveriesCommand(
    IReadOnlyList<Guid>? DeliveryIds = null,
    BulkRetryFilter? Filter = null) : IRequest<BulkRetryResult>
{
    public const int MaxIds = 200;
    public const int MaxMatches = 1000;
}

public sealed record BulkRetryFilter(
    IReadOnlyList<DeliveryStatus>? Statuses = null,
    NotificationChannel? Channel = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record SkippedDelivery(Guid DeliveryId, DeliveryRetryRefusal Reason);

public sealed record BulkRetryResult(int Requested, int Retried, IReadOnlyList<SkippedDelivery> Skipped);
