using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>
/// An in-memory delivery queue for the processing tests: the claim, release and lookup behave like the
/// real repository's conditional updates, and a save is only counted (the aggregate is the same object
/// the processor mutates), unless a test makes it throw.
/// </summary>
internal sealed class FakeDeliveryStore(FakeTimeProvider time) : INotificationRepository, INotificationOutboxStore, IUnitOfWork
{
    private static readonly NotificationPriority[] HighToLow =
        [NotificationPriority.Critical, NotificationPriority.High, NotificationPriority.Normal, NotificationPriority.Low];

    public List<Notification> Notifications { get; } = [];

    public Dictionary<Guid, NotificationOutboxEvent> Events { get; } = [];

    public int Saves { get; private set; }

    public Exception? SaveFailure { get; set; }

    public int ClaimCalls { get; private set; }

    /// <summary>Every delivery id the service asked to hand back unsent.</summary>
    public List<Guid> ReleasedIds { get; } = [];

    public List<IReadOnlyCollection<NotificationChannel>> ClaimedChannels { get; } = [];

    public List<int> ClaimBatchSizes { get; } = [];

    private DateTime Now => time.GetUtcNow().UtcDateTime;

    public IEnumerable<NotificationDelivery> AllDeliveries => Notifications.SelectMany(n => n.Deliveries);

    public NotificationDelivery Delivery(NotificationChannel channel = NotificationChannel.Email) =>
        AllDeliveries.Single(d => d.Channel == channel);

    // --- INotificationRepository: the delivery queue ---

    public Task<IReadOnlyList<Guid>> ClaimDueDeliveriesAsync(
        string workerId, IReadOnlyCollection<NotificationChannel> channels, DateTime leaseExpiresAtUtc, int batchSize, DateTime now, CancellationToken cancellationToken)
    {
        ClaimCalls++;
        ClaimBatchSizes.Add(batchSize);
        ClaimedChannels.Add(channels);
        var claimed = new List<Guid>();
        foreach (var priority in HighToLow)
        {
            foreach (var delivery in AllDeliveries
                .Where(d => d.Priority == priority
                    && d.Status is DeliveryStatus.Pending or DeliveryStatus.Retrying
                    && d.NextAttemptAtUtc <= now
                    && channels.Contains(d.Channel))
                .OrderBy(d => d.NextAttemptAtUtc)
                .ToList())
            {
                if (claimed.Count >= batchSize)
                {
                    break;
                }

                delivery.MarkSending(workerId, leaseExpiresAtUtc, now);
                claimed.Add(delivery.Id);
            }
        }

        return Task.FromResult<IReadOnlyList<Guid>>(claimed);
    }

    public Task<Notification?> GetClaimedDeliveryAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(n => n.Deliveries.Any(d =>
            d.Id == deliveryId && d.Status == DeliveryStatus.Sending && d.LeaseOwner == workerId)));

    public Task<int> ReleaseClaimsAsync(string workerId, IReadOnlyCollection<Guid> deliveryIds, DateTime now, CancellationToken cancellationToken)
    {
        ReleasedIds.AddRange(deliveryIds);
        var released = 0;
        foreach (var delivery in AllDeliveries.Where(d => deliveryIds.Contains(d.Id) && d.Status == DeliveryStatus.Sending && d.LeaseOwner == workerId))
        {
            delivery.Defer(now, now);
            released++;
        }

        return Task.FromResult(released);
    }

    public Task<int> SweepExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken)
    {
        var swept = 0;
        foreach (var delivery in AllDeliveries.Where(d => d.Status == DeliveryStatus.Sending && d.LeaseExpiresAtUtc <= now))
        {
            delivery.Fail(DeliveryFailureKind.AmbiguousOutcome, "swept", null, now);
            swept++;
        }

        return Task.FromResult(swept);
    }

    public Task<DeliveryBacklog> GetDueBacklogAsync(DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(new DeliveryBacklog(0, null));

    // --- INotificationOutboxStore ---

    public Task<NotificationOutboxEvent?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Events.GetValueOrDefault(id));

    public void Add(NotificationOutboxEvent outboxEvent) => Events[outboxEvent.Id] = outboxEvent;

    public Task<IReadOnlyList<Guid>> ClaimBatchAsync(string workerId, DateTime leaseExpiresAtUtc, int batchSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<NotificationOutboxEvent?> GetClaimedAsync(Guid id, string workerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> ReleaseClaimsAsync(string workerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    // --- IUnitOfWork ---

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SaveFailure is { } failure)
        {
            throw failure;
        }

        Saves++;
        return Task.FromResult(1);
    }

    public Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    // --- the rest of INotificationRepository is not used by the delivery tests ---

    public void Add(Notification notification) => Notifications.Add(notification);

    public Task<IReadOnlyList<Notification>> GetByDeliveryIdsAsync(IReadOnlyCollection<Guid> deliveryIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Notifications.SingleOrDefault(n => n.Id == id));

    public Task<IReadOnlySet<string>> GetRecipientsWithEventKeyAsync(string eventKey, IReadOnlyCollection<string> recipientUserIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> ExistsForAddressAsync(string eventKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, int>> CountUnreadAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountUnreadAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<(IReadOnlyList<Notification> Items, string? NextCursor)> ListAsync(
        string userId, IReadOnlyCollection<NotificationCategory>? categories, NotificationReadState state, string? cursor, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> MarkAllReadAsync(string userId, NotificationCategory? category, DateTime now, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
