using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications;

/// <summary>contracts/notifications-api.md GET /users/me/notification-preferences: the catalogue defaults merged with the caller's overrides.</summary>
public sealed record NotificationPreferencesDto(IReadOnlyList<NotificationCategoryPreferenceDto> Categories);

/// <summary>One category row. Frequency is always Immediate for now: digests are defined but not offered (FR-033).</summary>
public sealed record NotificationCategoryPreferenceDto(
    NotificationCategory Category,
    IReadOnlyList<NotificationChannelPreferenceDto> Channels,
    DeliveryFrequency Frequency,
    IReadOnlyList<DeliveryFrequency> AvailableFrequencies);

/// <param name="Locked">Mandatory: every emitted type in the category makes this channel mandatory (FR-032), so it can't be turned off.</param>
public sealed record NotificationChannelPreferenceDto(NotificationChannel Channel, bool Enabled, bool Locked);
