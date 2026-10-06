using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Sparse preference overrides: a missing row means the catalogue default applies.</summary>
public interface INotificationPreferenceRepository
{
    public const string UserCategoryChannelIndexName = "UX_NotificationPreferences_User_Category_Channel";

    Task<IReadOnlyList<PreferenceOverride>> GetOverridesAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Every override of every listed user; users with none are omitted.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<PreferenceOverride>>> GetOverridesForUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>The user's override rows, tracked so an update can change them in place.</summary>
    Task<IReadOnlyList<NotificationPreference>> GetTrackedAsync(string userId, CancellationToken cancellationToken);

    void Add(NotificationPreference preference);

    /// <summary>
    /// Removes the user's overrides for the given pairs, for real: an override is a sparse row, and a
    /// soft-deleted one would still count and would block re-creating the pair (unique index). Runs at once, outside the unit of work.
    /// </summary>
    Task DeleteAsync(
        string userId, IReadOnlyCollection<(NotificationCategory Category, NotificationChannel Channel)> pairs, CancellationToken cancellationToken);
}
