using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>The transactional outbox (research R2–R4). Claims are leases, so a crashed worker's events are picked up again.</summary>
public interface INotificationOutboxStore
{
    /// <summary>Adds the event to the current unit of work; the caller's save commits it.</summary>
    void Add(NotificationOutboxEvent outboxEvent);

    /// <summary>
    /// Leases up to <paramref name="batchSize"/> due events (pending ones whose next attempt is due,
    /// and processing ones whose lease has expired) to <paramref name="workerId"/> in one atomic
    /// statement, so two workers never claim the same event. Returns the claimed ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> ClaimBatchAsync(string workerId, DateTime leaseExpiresAtUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>The event, tracked, only while <paramref name="workerId"/> still holds its lease; otherwise null.</summary>
    Task<NotificationOutboxEvent?> GetClaimedAsync(Guid id, string workerId, CancellationToken cancellationToken);

    /// <summary>Returns every event still leased to <paramref name="workerId"/> to pending, for shutdown. Returns how many were released.</summary>
    Task<int> ReleaseClaimsAsync(string workerId, CancellationToken cancellationToken);
}
