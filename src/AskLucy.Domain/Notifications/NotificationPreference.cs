using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// A sparse per-user override of one <c>(category, channel)</c> pair (FR-031–FR-034). With no row,
/// each type uses its catalogue default; with a row, every optional type in the category that
/// supports the channel follows it (research R28).
/// </summary>
public sealed class NotificationPreference : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public NotificationCategory Category { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public bool IsEnabled { get; private set; }

    public DeliveryFrequency Frequency { get; private set; }

    private NotificationPreference()
    {
        // Required by EF Core materialization.
    }

    public static NotificationPreference Create(
        string userId,
        NotificationCategory category,
        NotificationChannel channel,
        bool isEnabled,
        DeliveryFrequency frequency,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        EnsureConfigurable(category, channel);
        EnsureFrequency(frequency);

        return new NotificationPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Category = category,
            Channel = channel,
            IsEnabled = isEnabled,
            Frequency = frequency,
            CreatedAtUtc = now,
        };
    }

    public void Update(bool isEnabled, DeliveryFrequency frequency)
    {
        EnsureConfigurable(Category, Channel);
        EnsureFrequency(frequency);
        IsEnabled = isEnabled;
        Frequency = frequency;
    }

    /// <summary>Mandatory pairs are locked (FR-032); pairs no emitted type uses have nothing to configure.</summary>
    private static void EnsureConfigurable(NotificationCategory category, NotificationChannel channel)
    {
        if (!NotificationTypeCatalog.IsConfigurable(category, channel))
        {
            throw new DomainRuleViolationException($"{category} notifications by {channel} can't be changed.");
        }
    }

    /// <summary>Digests are defined but not offered yet (FR-033).</summary>
    private static void EnsureFrequency(DeliveryFrequency frequency)
    {
        if (frequency != DeliveryFrequency.Immediate)
        {
            throw new DomainRuleViolationException("Only immediate delivery is available.");
        }
    }
}
