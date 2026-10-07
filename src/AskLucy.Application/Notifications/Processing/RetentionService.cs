using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>What one retention run removed.</summary>
public sealed record RetentionResult(
    int ReadNotifications,
    int OwnerDeletedNotifications,
    int FailedDeliveries,
    int FinishedDeliveries,
    int CompletedOutboxEvents,
    int FailedOutboxEvents)
{
    public int Total => ReadNotifications + OwnerDeletedNotifications + FailedDeliveries + FinishedDeliveries + CompletedOutboxEvents + FailedOutboxEvents;
}

/// <summary>
/// The daily clean-up (FR-059, research R20). Each class of data is deleted after its own window, in batches, until none
/// is left. Audit rows are never touched. Singleton: each batch runs in its own scope, so a long run holds no change tracker.
/// </summary>
public sealed class RetentionService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger)
{
    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var retention = options.CurrentValue.Retention;
        var batch = Math.Clamp(retention.BatchSize, 1, 5000);

        var result = new RetentionResult(
            await DrainAsync((repo, ct) => repo.DeleteReadNotificationsAsync(now.AddDays(-Math.Max(1, retention.ReadDays)), batch, ct), batch, cancellationToken),
            await DrainAsync((repo, ct) => repo.DeleteOwnerDeletedNotificationsAsync(now.AddDays(-Math.Max(1, retention.DeletedDays)), batch, ct), batch, cancellationToken),
            await DrainAsync((repo, ct) => repo.DeleteFailedDeliveriesAsync(now.AddDays(-Math.Max(1, retention.FailedDays)), batch, ct), batch, cancellationToken),
            await DrainAsync((repo, ct) => repo.DeleteFinishedDeliveriesAsync(now.AddDays(-Math.Max(1, retention.DeliveryDays)), batch, ct), batch, cancellationToken),
            await DrainAsync((repo, ct) => repo.DeleteCompletedOutboxEventsAsync(now.AddDays(-Math.Max(1, retention.CompletedOutboxDays)), batch, ct), batch, cancellationToken),
            await DrainAsync((repo, ct) => repo.DeleteFailedOutboxEventsAsync(now.AddDays(-Math.Max(1, retention.FailedDays)), batch, ct), batch, cancellationToken));

        DeliveryLog.RetentionRan(logger, result.ReadNotifications, result.OwnerDeletedNotifications, result.FailedDeliveries, result.FinishedDeliveries, result.CompletedOutboxEvents, result.FailedOutboxEvents);
        return result;
    }

    /// <summary>Runs one delete until a batch comes back short, each batch in a scope of its own.</summary>
    private async Task<int> DrainAsync(
        Func<INotificationRetentionRepository, CancellationToken, Task<int>> deleteBatch, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var removed = await deleteBatch(scope.ServiceProvider.GetRequiredService<INotificationRetentionRepository>(), cancellationToken);
            total += removed;
            if (removed < batchSize)
            {
                return total;
            }
        }
    }
}
