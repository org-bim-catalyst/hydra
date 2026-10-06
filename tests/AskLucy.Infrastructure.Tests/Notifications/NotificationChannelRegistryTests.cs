using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Notifications;

/// <summary>
/// <see cref="NotificationChannelRegistry"/> (specs/067 FR-060, T114/T120): the channels routing may use are
/// in-app when enabled plus every channel that has a registered sender and isn't switched off, so email joins
/// the moment its sender is registered, and a channel switched off in configuration is skipped, not queued.
/// </summary>
public sealed class NotificationChannelRegistryTests
{
    private static NotificationChannelRegistry CreateSut(NotificationsOptions options, params NotificationChannel[] senders)
    {
        var monitor = Substitute.For<IOptionsMonitor<NotificationsOptions>>();
        monitor.CurrentValue.Returns(options);
        return new NotificationChannelRegistry(monitor, senders.Select(c => new RegisteredChannelSender(c)));
    }

    [Fact]
    public void WithNoSender_OnlyInAppIsAvailable()
    {
        CreateSut(new NotificationsOptions()).AvailableChannels.Should().BeEquivalentTo([NotificationChannel.InApp]);
    }

    [Fact]
    public void WithAnEmailSender_EmailJoinsInApp()
    {
        CreateSut(new NotificationsOptions(), NotificationChannel.Email).AvailableChannels
            .Should().BeEquivalentTo([NotificationChannel.InApp, NotificationChannel.Email]);
    }

    [Fact]
    public void EmailSwitchedOffInConfiguration_IsNotAvailable_EvenWithASender()
    {
        var options = new NotificationsOptions { Channels = new NotificationChannelsOptions { Email = new NotificationChannelToggle { Enabled = false } } };

        CreateSut(options, NotificationChannel.Email).AvailableChannels.Should().BeEquivalentTo([NotificationChannel.InApp]);
    }

    [Fact]
    public void InAppSwitchedOff_LeavesTheSenderChannelsAlone()
    {
        var options = new NotificationsOptions { Channels = new NotificationChannelsOptions { InApp = new NotificationChannelToggle { Enabled = false } } };

        CreateSut(options, NotificationChannel.Email).AvailableChannels.Should().BeEquivalentTo([NotificationChannel.Email]);
    }

    [Fact]
    public void TheSameSenderRegisteredTwice_ListsItsChannelOnce()
    {
        CreateSut(new NotificationsOptions(), NotificationChannel.Email, NotificationChannel.Email).AvailableChannels.Should().HaveCount(2);
    }
}
