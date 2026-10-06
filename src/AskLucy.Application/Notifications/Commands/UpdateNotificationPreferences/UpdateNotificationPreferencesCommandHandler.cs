using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;

namespace AskLucy.Application.Notifications.Commands.UpdateNotificationPreferences;

/// <summary>
/// Storage is sparse (FR-031): a change that equals the default removes the override, anything else
/// saves one. Mandatory pairs are enforced here, on the server (FR-032), whatever the client showed.
/// </summary>
public sealed class UpdateNotificationPreferencesCommandHandler(
    INotificationPreferenceRepository preferences,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IRequestHandler<UpdateNotificationPreferencesCommand, NotificationPreferencesDto>
{
    private const string LockedMessage = "This notification can't be turned off.";

    public async Task<NotificationPreferencesDto> Handle(UpdateNotificationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        RejectLockedPairs(request.Changes);

        var toDelete = new List<(NotificationCategory, NotificationChannel)>();

        // Two concurrent first-time saves of the same pair race on the unique index; the loser re-reads and applies again.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            toDelete.Clear();
            var existing = await preferences.GetTrackedAsync(userId, cancellationToken);
            ApplyChanges(userId, request.Changes, existing, toDelete);

            if (await unitOfWork.TrySaveChangesAsync(INotificationPreferenceRepository.UserCategoryChannelIndexName, cancellationToken))
            {
                break;
            }

            if (attempt == 1)
            {
                throw new InvalidOperationException("Notification preferences were changed by another request; try again.");
            }
        }

        if (toDelete.Count > 0)
        {
            await preferences.DeleteAsync(userId, toDelete, cancellationToken);
        }

        return NotificationPreferencesBuilder.Build(await preferences.GetOverridesAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Switching a locked or unused pair off is refused; switching it on is already true, so it is accepted and stores nothing.
    /// </summary>
    private static void RejectLockedPairs(IReadOnlyList<NotificationPreferenceChange> changes)
    {
        Dictionary<string, string[]>? errors = null;
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            if (!change.Enabled && !NotificationTypeCatalog.IsConfigurable(change.Category, change.Channel))
            {
                (errors ??= [])[$"changes[{i}]"] = [LockedMessage];
            }
        }

        if (errors is not null)
        {
            throw new NotificationPreferenceRejectedException(errors);
        }
    }

    private void ApplyChanges(
        string userId,
        IReadOnlyList<NotificationPreferenceChange> changes,
        IReadOnlyList<NotificationPreference> existing,
        List<(NotificationCategory, NotificationChannel)> toDelete)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var change in changes)
        {
            if (!NotificationTypeCatalog.IsConfigurable(change.Category, change.Channel))
            {
                continue;
            }

            var current = existing.FirstOrDefault(p => p.Category == change.Category && p.Channel == change.Channel);
            if (change.Enabled == NotificationTypeCatalog.DefaultEnabled(change.Category, change.Channel))
            {
                if (current is not null)
                {
                    toDelete.Add((change.Category, change.Channel));
                }

                continue;
            }

            if (current is null)
            {
                preferences.Add(NotificationPreference.Create(
                    userId, change.Category, change.Channel, change.Enabled, DeliveryFrequency.Immediate, now));
            }
            else
            {
                current.Update(change.Enabled, DeliveryFrequency.Immediate);
            }
        }
    }
}
