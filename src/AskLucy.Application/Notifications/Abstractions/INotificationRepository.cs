using AskLucy.Application.Notifications;
using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

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
}
