using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>Merges the catalogue with a user's sparse overrides into what the settings screen shows (FR-031–FR-034).</summary>
public static class NotificationPreferencesBuilder
{
    private static readonly DeliveryFrequency[] Offered = [DeliveryFrequency.Immediate];

    public static NotificationPreferencesDto Build(IReadOnlyCollection<PreferenceOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var categories = new List<NotificationCategoryPreferenceDto>();
        foreach (var category in NotificationTypeCatalog.EmittedCategories())
        {
            var channels = new List<NotificationChannelPreferenceDto>();
            foreach (var channel in Enum.GetValues<NotificationChannel>())
            {
                if (!NotificationTypeCatalog.IsUsed(category, channel))
                {
                    continue;
                }

                channels.Add(new NotificationChannelPreferenceDto(channel, IsEnabled(category, channel, overrides), NotificationTypeCatalog.IsLocked(category, channel)));
            }

            categories.Add(new NotificationCategoryPreferenceDto(category, channels, DeliveryFrequency.Immediate, Offered));
        }

        return new NotificationPreferencesDto(categories);
    }

    private static bool IsEnabled(NotificationCategory category, NotificationChannel channel, IReadOnlyCollection<PreferenceOverride> overrides)
    {
        if (NotificationTypeCatalog.IsLocked(category, channel))
        {
            return true;
        }

        var saved = overrides.FirstOrDefault(o => o.Category == category && o.Channel == channel);
        return saved?.IsEnabled ?? NotificationTypeCatalog.DefaultEnabled(category, channel);
    }
}
