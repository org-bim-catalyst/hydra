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
}
