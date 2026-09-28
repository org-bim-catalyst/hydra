using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AskLucy.Persistence.Interceptors;

/// <summary>
/// specs/067 research R3. Wakes the dispatcher when a save commits a new outbox event, and the
/// delivery worker when it commits new deliveries, so neither waits out its poll interval. The pulse
/// is sent only after the commit: a rolled-back save wakes nobody, and a missed pulse costs one poll.
/// </summary>
public sealed class NotificationWakeInterceptor(INotificationWakeSignal wakeSignal) : SaveChangesInterceptor
{
    private bool _outboxEventAdded;
    private bool _deliveryAdded;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Inspect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Inspect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Pulse();
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Pulse();
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Reset();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        Reset();
        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset();
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private void Inspect(DbContext? context)
    {
        Reset();
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            _outboxEventAdded |= entry.Entity is NotificationOutboxEvent;
            _deliveryAdded |= entry.Entity is NotificationDelivery;
        }
    }

    private void Pulse()
    {
        if (_outboxEventAdded)
        {
            wakeSignal.PulseDispatcher();
        }

        if (_deliveryAdded)
        {
            wakeSignal.PulseDeliveryWorker();
        }

        Reset();
    }

    private void Reset()
    {
        _outboxEventAdded = false;
        _deliveryAdded = false;
    }
}
