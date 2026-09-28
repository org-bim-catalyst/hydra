namespace AskLucy.Infrastructure.Notifications.Workers;

/// <summary>
/// When each notification worker last completed a loop iteration (FR-057). A singleton the workers
/// write and the <c>notifications-*</c> health checks read, so neither depends on the other.
/// </summary>
public sealed class NotificationWorkerHeartbeats
{
    private long _dispatcherTicks;

    /// <summary>Null until the dispatcher's first iteration.</summary>
    public DateTimeOffset? Dispatcher => Read(ref _dispatcherTicks);

    public void RecordDispatcher(DateTimeOffset at) => Interlocked.Exchange(ref _dispatcherTicks, at.UtcTicks);

    private static DateTimeOffset? Read(ref long ticks)
    {
        var value = Interlocked.Read(ref ticks);
        return value == 0 ? null : new DateTimeOffset(value, TimeSpan.Zero);
    }
}
