using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/067 research R20. Every delete is a bounded, set-based statement (a keyset of ids, then one DELETE), and none touches
/// <see cref="NotificationAuditLog"/>. A notification's deliveries go with it through the foreign key's cascade.
/// </summary>
public sealed class NotificationRetentionRepository(AskLucyDbContext dbContext) : INotificationRetentionRepository
{
    private static readonly DeliveryStatus[] Active = [DeliveryStatus.Pending, DeliveryStatus.Sending, DeliveryStatus.Retrying];

    private DbSet<NotificationDelivery> Deliveries() => dbContext.Set<NotificationDelivery>();

    public async Task<int> DeleteReadNotificationsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
        await DeleteNotificationsAsync(
            dbContext.Notifications.IgnoreQueryFilters().Where(n => n.ReadAtUtc != null && n.ReadAtUtc < cutoffUtc), batchSize, cancellationToken);

    public async Task<int> DeleteOwnerDeletedNotificationsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
        await DeleteNotificationsAsync(
            dbContext.Notifications.IgnoreQueryFilters().Where(n => n.DeletedAtUtc != null && n.DeletedAtUtc < cutoffUtc), batchSize, cancellationToken);

    private async Task<int> DeleteNotificationsAsync(IQueryable<Notification> candidates, int batchSize, CancellationToken cancellationToken)
    {
        // A notification that still has work in flight is skipped, so an active delivery is never orphaned.
        var ids = await candidates
            .Where(n => !Deliveries().Any(d => d.NotificationId == n.Id && Active.Contains(d.Status)))
            .OrderBy(n => n.Id)
            .Select(n => n.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return ids.Count == 0
            ? 0
            : await dbContext.Notifications.IgnoreQueryFilters().Where(n => ids.Contains(n.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteFailedDeliveriesAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await Deliveries()
            .Where(d => (d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.DeadLettered)
                && (d.LastAttemptAtUtc ?? d.CreatedAtUtc) < cutoffUtc)
            .OrderBy(d => d.Id).Select(d => d.Id).Take(batchSize).ToListAsync(cancellationToken);

        return ids.Count == 0 ? 0 : await Deliveries().Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteFinishedDeliveriesAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await Deliveries()
            .Where(d => d.Channel != NotificationChannel.InApp
                && d.CreatedAtUtc < cutoffUtc
                && (d.Status == DeliveryStatus.Sent || d.Status == DeliveryStatus.Delivered || d.Status == DeliveryStatus.Skipped
                    || d.Status == DeliveryStatus.Cancelled || d.Status == DeliveryStatus.Expired))
            .OrderBy(d => d.Id).Select(d => d.Id).Take(batchSize).ToListAsync(cancellationToken);

        return ids.Count == 0 ? 0 : await Deliveries().Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteCompletedOutboxEventsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dbContext.NotificationOutboxEvents
            .Where(e => e.Status == OutboxEventStatus.Completed && e.ProcessedAtUtc != null && e.ProcessedAtUtc < cutoffUtc)
            .OrderBy(e => e.Id).Select(e => e.Id).Take(batchSize).ToListAsync(cancellationToken);

        return ids.Count == 0 ? 0 : await dbContext.NotificationOutboxEvents.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteFailedOutboxEventsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await dbContext.NotificationOutboxEvents
            .Where(e => e.Status == OutboxEventStatus.Failed && e.ProcessedAtUtc != null && e.ProcessedAtUtc < cutoffUtc)
            .OrderBy(e => e.Id).Select(e => e.Id).Take(batchSize).ToListAsync(cancellationToken);

        return ids.Count == 0 ? 0 : await dbContext.NotificationOutboxEvents.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
