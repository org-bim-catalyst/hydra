using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Domain.Notifications;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.BulkRetryNotificationDeliveries;

public sealed class BulkRetryNotificationDeliveriesCommandHandler(
    INotificationRepository notifications,
    INotificationAdminRepository adminRepository,
    INotificationRecipientDirectory directory,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<BulkRetryNotificationDeliveriesCommand, BulkRetryResult>
{
    public async Task<BulkRetryResult> Handle(BulkRetryNotificationDeliveriesCommand request, CancellationToken cancellationToken)
    {
        var deliveryIds = await ResolveIdsAsync(request, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var policy = new DeliveryRetryPolicy(directory);

        var owners = await notifications.GetByDeliveryIdsAsync(deliveryIds, cancellationToken);
        var byDelivery = owners
            .SelectMany(n => n.Deliveries.Select(d => (Notification: n, Delivery: d)))
            .Where(x => deliveryIds.Contains(x.Delivery.Id))
            .ToDictionary(x => x.Delivery.Id);

        var skipped = new List<SkippedDelivery>();
        var retried = 0;
        foreach (var id in deliveryIds)
        {
            if (!byDelivery.TryGetValue(id, out var pair))
            {
                // Gone since it was listed: a delivery with no notification can't be retried either.
                skipped.Add(new SkippedDelivery(id, DeliveryRetryRefusal.NotificationDeleted));
                continue;
            }

            if (await policy.RefusalAsync(pair.Notification, pair.Delivery, now, cancellationToken) is { } refusal)
            {
                skipped.Add(new SkippedDelivery(id, refusal));
                continue;
            }

            pair.Notification.RetryDelivery(id, now);
            retried++;
        }

        // One audit row for the whole action, with the counts (FR-054).
        audit.Write(
            NotificationAuditAction.DeliveriesBulkRetried,
            "NotificationDelivery",
            "bulk",
            NotificationAuditOutcome.Succeeded,
            new
            {
                requested = deliveryIds.Count,
                retried,
                skipped = skipped.Count,
                by = request.DeliveryIds is not null ? "ids" : "filter",
            });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new BulkRetryResult(deliveryIds.Count, retried, skipped);
    }

    private async Task<IReadOnlyList<Guid>> ResolveIdsAsync(BulkRetryNotificationDeliveriesCommand request, CancellationToken cancellationToken)
    {
        if (request.DeliveryIds is { } ids)
        {
            return [.. ids.Distinct()];
        }

        var filter = request.Filter!;
        var statuses = filter.Statuses is { Count: > 0 } ? filter.Statuses : [DeliveryStatus.Failed, DeliveryStatus.DeadLettered];
        var matches = await adminRepository.FindDeliveryIdsAsync(
            new AdminDeliveryFilter(statuses, filter.Channel, Category: null, Type: null, filter.FromUtc, filter.ToUtc),
            BulkRetryNotificationDeliveriesCommand.MaxMatches + 1,
            cancellationToken);

        if (matches.Count > BulkRetryNotificationDeliveriesCommand.MaxMatches)
        {
            throw new ValidationException(
                [new ValidationFailure("filter", $"The filter matches more than {BulkRetryNotificationDeliveriesCommand.MaxMatches} deliveries. Narrow it and try again.")]);
        }

        return matches;
    }
}
