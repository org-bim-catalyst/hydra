#pragma warning disable CS0618 // The shim under test is obsolete by design: it exists for one release.

using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// specs/067 US9-B (T133), replacing specs/058-password-recovery's job tests: <see cref="PasswordEmailJob"/> is
/// a one-release forwarding shim. A reset-link job enqueued before the deploy carries a token that was
/// already issued; the shim ignores it and asks the hub, which mints a fresh link when it sends and so
/// supersedes the old one.
/// </summary>
public sealed class PasswordEmailJobTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task SendResetLinkAsync_ShouldPublishAResetRequestForTheAccount_WithoutUsingTheQueuedToken()
    {
        var job = new PasswordEmailJob(_publisher, _unitOfWork);

        await job.SendResetLinkAsync("user-1", "user@example.com", "protected-token-ciphertext", CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountPasswordResetRequested
                && r.Recipient == new NotificationRecipient.User("user-1")
                && r.Variables.Count == 0));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SendPasswordChangedNoticeAsync_ShouldPublishThePasswordChangedNotification_WithTheTimeItHappened()
    {
        var job = new PasswordEmailJob(_publisher, _unitOfWork);

        await job.SendPasswordChangedNoticeAsync("user@example.com", new DateTime(2026, 10, 6, 9, 30, 0, DateTimeKind.Utc), CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r.Type == NotificationTypeKeys.SecurityPasswordChanged
            && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")
            && r.Variables["changedAt"] == "2026-10-06 09:30 UTC"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Shims_AreMarkedObsolete_SoNothingNewStartsUsingThem()
    {
        foreach (var type in new[] { typeof(PasswordEmailJob), typeof(AccountEmailJob), typeof(AskLucy.Application.Authentication.PasswordReset.PasswordResetIssuanceJob) })
        {
            var obsolete = type.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Cast<ObsoleteAttribute>().SingleOrDefault();
            obsolete.Should().NotBeNull($"{type.Name} is a one-release shim");
            obsolete!.Message.Should().Be("Removed in the release after 067; forwards to the notification hub.");
        }
    }
}
