namespace AskLucy.Infrastructure.Notifications.Workers;

/// <summary>
/// When each notification worker last completed a loop iteration (FR-057). A singleton the workers
/// write and the <c>notifications-*</c> health checks read, so neither depends on the other.
/// </summary>
public sealed class NotificationWorkerHeartbeats(TimeProvider? timeProvider = null)
{
    /// <summary>When this process created the heartbeat store: the grace point for a worker that hasn't had a first pass yet.</summary>
    public DateTimeOffset StartedAt { get; } = (timeProvider ?? TimeProvider.System).GetUtcNow();

    private long _dispatcherTicks;
    private long _deliveryWorkerTicks;

    /// <summary>Null until the dispatcher's first iteration.</summary>
    public DateTimeOffset? Dispatcher => Read(ref _dispatcherTicks);

    /// <summary>Null until the delivery worker's first iteration.</summary>
    public DateTimeOffset? DeliveryWorker => Read(ref _deliveryWorkerTicks);

    public void RecordDispatcher(DateTimeOffset at) => Interlocked.Exchange(ref _dispatcherTicks, at.UtcTicks);

    public void RecordDeliveryWorker(DateTimeOffset at) => Interlocked.Exchange(ref _deliveryWorkerTicks, at.UtcTicks);

    private static DateTimeOffset? Read(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}
