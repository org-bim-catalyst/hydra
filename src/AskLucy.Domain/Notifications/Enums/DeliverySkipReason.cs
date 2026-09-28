namespace AskLucy.Domain.Notifications;

/// <summary>Why the router left a channel out at materialization.</summary>
public enum DeliverySkipReason
{
    PreferenceDisabled,
    ChannelDisabled,
    NoVerifiedAddress,
    NotCritical,
}
