using AskLucy.Application.Notifications;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>How much sendable work is waiting (FR-057 backlog check and statistics).</summary>
/// <param name="DueCount">Deliveries pending or retrying whose attempt time has passed.</param>
/// <param name="OldestDueAtUtc">The earliest attempt time among them; null when there are none.</param>
public sealed record DeliveryBacklog(int DueCount, DateTime? OldestDueAtUtc);

/// <summary>Persistence for the <see cref="Notification"/> aggregate and its deliveries.</summary>
public interface INotificationRepository
{
    /// <summary>The unique index that makes a replayed event a no-op (R22).</summary>
    public const string RecipientEventKeyIndexName = "UX_Notifications_Recipient_EventKey";

    void Add(Notification notification);

    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="recipientUserIds"/> already hold a notification for <paramref name="eventKey"/>.</summary>
    Task<IReadOnlySet<string>> GetRecipientsWithEventKeyAsync(string eventKey, IReadOnlyCollection<string> recipientUserIds, CancellationToken cancellationToken);

    /// <summary>Whether a recipient-less notification (support mailbox) already exists for <paramref name="eventKey"/>.</summary>
    Task<bool> ExistsForAddressAsync(string eventKey, CancellationToken cancellationToken);

    /// <summary>Unread notification-center items (not deleted, not expired) per user; users with none are omitted.</summary>
    Task<IReadOnlyDictionary<string, int>> CountUnreadAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>Unread notification-center items for a single user (research R19: index-only count).</summary>
    Task<int> CountUnreadAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// Keyset-paginated notification center list for <paramref name="userId"/>, newest first
    /// (FR-014, FR-015, contracts/notifications-api.md). <paramref name="categories"/> null or
    /// empty means every category.
    /// </summary>
    Task<(IReadOnlyList<Notification> Items, string? NextCursor)> ListAsync(
        string userId,
        IReadOnlyCollection<NotificationCategory>? categories,
        NotificationReadState state,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks every unread, deliverable notification for <paramref name="userId"/> read in one
    /// set-based update (contracts/notifications-api.md `mark-all-read`), optionally limited to
    /// one category. Returns the number of rows updated.
    /// </summary>
    Task<int> MarkAllReadAsync(string userId, NotificationCategory? category, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// specs/067 T099 — explicit, set-based purge of every <see cref="Notification"/> (and, via the
    /// FK, its <see cref="NotificationDelivery"/> rows) for <paramref name="userId"/>, called from
    /// account deletion before the <c>ApplicationUser</c> row itself is removed. The database's own
    /// <c>ON DELETE CASCADE</c> (data-model.md § Relationships) would do this too, but account
    /// deletion doesn't rely on that alone — it deletes explicitly, matching the
    /// audit-log/legacy-notification anonymization steps already run here.
    /// </summary>
    Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken);

    // --- delivery queue (specs/067 research R4, R5) ---

    /// <summary>
    /// Leases up to <paramref name="batchSize"/> due deliveries on <paramref name="channels"/> to
    /// <paramref name="workerId"/>, highest priority first, each by one conditional update to
    /// <see cref="DeliveryStatus.Sending"/> that counts the attempt, so two workers never claim the same
    /// delivery. Pending and retrying deliveries are due when their attempt time has passed. Returns the
    /// claimed delivery ids.
    /// </summary>
    Task<IReadOnlyList<Guid>> ClaimDueDeliveriesAsync(
        string workerId,
        IReadOnlyCollection<NotificationChannel> channels,
        DateTime leaseExpiresAtUtc,
        int batchSize,
        DateTime now,
        CancellationToken cancellationToken);

    /// <summary>
    /// The notification (tracked, with every delivery) that owns <paramref name="deliveryId"/>, only
    /// while the delivery is still <see cref="DeliveryStatus.Sending"/> and leased to
    /// <paramref name="workerId"/>; otherwise null. Ignores the owner-deletion filter, so the worker can
    /// cancel the send of a notification its owner deleted.
    /// </summary>
    Task<Notification?> GetClaimedDeliveryAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken);

    /// <summary>
    /// Hands claimed deliveries back unsent, for graceful shutdown: each goes back to pending (or retrying)
    /// and returns the attempt the claim counted. Only <paramref name="deliveryIds"/> still leased to
    /// <paramref name="workerId"/> move. Returns how many were released.
    /// </summary>
    Task<int> ReleaseClaimsAsync(string workerId, IReadOnlyCollection<Guid> deliveryIds, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// A delivery still <see cref="DeliveryStatus.Sending"/> past its lease crashed mid-send, so its outcome
    /// is unknown: it becomes <see cref="DeliveryStatus.Failed"/> with
    /// <see cref="DeliveryFailureKind.AmbiguousOutcome"/> and is never requeued (R5). The owning
    /// notifications' aggregate status is brought up to date. Returns how many deliveries were swept.
    /// </summary>
    Task<int> SweepExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken);

    Task<DeliveryBacklog> GetDueBacklogAsync(DateTime now, CancellationToken cancellationToken);
}
