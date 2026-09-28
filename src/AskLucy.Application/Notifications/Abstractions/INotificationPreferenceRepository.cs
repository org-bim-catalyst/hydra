using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>Sparse preference overrides: a missing row means the catalogue default applies.</summary>
public interface INotificationPreferenceRepository
{
    Task<IReadOnlyList<PreferenceOverride>> GetOverridesAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Every override of every listed user; users with none are omitted.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<PreferenceOverride>>> GetOverridesForUsersAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}
