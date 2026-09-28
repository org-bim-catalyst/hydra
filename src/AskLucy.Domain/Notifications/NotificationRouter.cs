namespace AskLucy.Domain.Notifications;

/// <summary>What the router needs to know about the recipient.</summary>
/// <param name="HasVerifiedEmail">
/// True when the email channel has somewhere to go: the user's confirmed address, or an explicit
/// address or the support mailbox for recipients that aren't users.
/// </param>
public sealed record RecipientRoutingState(bool HasVerifiedEmail);

/// <summary>A user's saved <c>(category, channel)</c> override (R28).</summary>
public sealed record PreferenceOverride(NotificationCategory Category, NotificationChannel Channel, bool IsEnabled);

/// <summary>The router's verdict for one channel the type supports.</summary>
public sealed record ChannelDecision(NotificationChannel Channel, bool Deliver, DeliverySkipReason? SkipReason)
{
    public static ChannelDecision Send(NotificationChannel channel) => new(channel, true, null);

    public static ChannelDecision Skip(NotificationChannel channel, DeliverySkipReason reason) => new(channel, false, reason);
}

/// <summary>
/// The FR-003 decision table as a pure function (research R7, R28). One decision per channel the
/// type supports; a channel the type doesn't use never appears.
/// </summary>
public static class NotificationRouter
{
    /// <param name="availableChannels">The registered senders enabled in configuration, plus <see cref="NotificationChannel.InApp"/> when it is enabled.</param>
    /// <param name="isCritical">For announcements: the administrator marked it critical (FR-004a).</param>
    public static IReadOnlyList<ChannelDecision> Route(
        NotificationTypeDefinition definition,
        RecipientRoutingState recipient,
        IReadOnlyCollection<PreferenceOverride> preferenceOverrides,
        IReadOnlySet<NotificationChannel> availableChannels,
        bool isCritical)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(preferenceOverrides);
        ArgumentNullException.ThrowIfNull(availableChannels);

        var decisions = new List<ChannelDecision>(definition.Channels.Count);
        foreach (var (channel, state) in definition.Channels.OrderBy(c => c.Key))
        {
            decisions.Add(Decide(definition, channel, state, recipient, preferenceOverrides, availableChannels, isCritical));
        }

        return decisions;
    }

    private static ChannelDecision Decide(
        NotificationTypeDefinition definition,
        NotificationChannel channel,
        ChannelDefault state,
        RecipientRoutingState recipient,
        IReadOnlyCollection<PreferenceOverride> preferenceOverrides,
        IReadOnlySet<NotificationChannel> availableChannels,
        bool isCritical)
    {
        // A channel that is switched off or has no sender can't deliver anything, mandatory or not.
        if (!availableChannels.Contains(channel))
        {
            return ChannelDecision.Skip(channel, DeliverySkipReason.ChannelDisabled);
        }

        if (channel == NotificationChannel.Email && definition.EmailOnlyWhenCritical && !isCritical)
        {
            return ChannelDecision.Skip(channel, DeliverySkipReason.NotCritical);
        }

        if (state != ChannelDefault.Mandatory && !IsEnabledFor(definition.Category, channel, state, preferenceOverrides))
        {
            return ChannelDecision.Skip(channel, DeliverySkipReason.PreferenceDisabled);
        }

        if (channel == NotificationChannel.Email && !recipient.HasVerifiedEmail)
        {
            return ChannelDecision.Skip(channel, DeliverySkipReason.NoVerifiedAddress);
        }

        return ChannelDecision.Send(channel);
    }

    /// <summary>R28: a saved category override wins for every optional type; otherwise the type's own default.</summary>
    private static bool IsEnabledFor(
        NotificationCategory category,
        NotificationChannel channel,
        ChannelDefault state,
        IReadOnlyCollection<PreferenceOverride> preferenceOverrides)
    {
        var saved = preferenceOverrides.FirstOrDefault(p => p.Category == category && p.Channel == channel);
        return saved?.IsEnabled ?? state == ChannelDefault.On;
    }
}
