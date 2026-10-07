namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>
/// Set-based deletes for the retention job (research R20). Each method removes at most <c>batchSize</c> rows and returns how many it
/// removed, so the caller loops until a short batch and no single statement holds locks for long on the shared host. Audit rows have
/// no method here, on purpose: they are never deleted.
/// </summary>
public interface INotificationRetentionRepository
{
    /// <summary>Notifications read before <paramref name="cutoffUtc"/>, with their deliveries. One with an active delivery is left alone.</summary>
    Task<int> DeleteReadNotificationsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Notifications their owner deleted before <paramref name="cutoffUtc"/>, with their deliveries. One with an active delivery is left alone.</summary>
    Task<int> DeleteOwnerDeletedNotificationsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Failed and dead-lettered deliveries whose last attempt was before <paramref name="cutoffUtc"/>.</summary>
    Task<int> DeleteFailedDeliveriesAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Finished deliveries on channels other than in-app, created before <paramref name="cutoffUtc"/>. An in-app delivery is the
    /// row that makes its notification appear in the center, so it lives and dies with its notification instead.
    /// </summary>
    Task<int> DeleteFinishedDeliveriesAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Outbox events completed before <paramref name="cutoffUtc"/>.</summary>
    Task<int> DeleteCompletedOutboxEventsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Outbox events that gave up (status Failed) before <paramref name="cutoffUtc"/>: kept this long for the administrators' backlog view.</summary>
    Task<int> DeleteFailedOutboxEventsAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);
}
