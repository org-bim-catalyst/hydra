#pragma warning disable CS0618 // The shim under test is obsolete by design: it exists for one release.

using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Email;
using NSubstitute;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Email;

/// <summary>
/// specs/067 US9-B (T133), replacing specs/061-branded-email-templates T009: <see cref="AccountEmailJob"/> is a
/// one-release forwarding shim. Hangfire jobs enqueued before the deploy still run with their old
/// signature, and now hand the request to the hub instead of building and sending the email.
/// </summary>
public sealed class AccountEmailJobTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task ResendConfirmationAsync_ShouldPublishAConfirmationRequestForTheAddress_AndCommitIt()
    {
        var job = new AccountEmailJob(_publisher, _unitOfWork);

        await job.ResendConfirmationAsync("user@example.com", CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountEmailConfirmationRequested
                && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task SendAccountSupportRequestAsync_ShouldPublishToTheSupportMailbox_WithTheSenderAndTheirMessage()
    {
        var job = new AccountEmailJob(_publisher, _unitOfWork);

        await job.SendAccountSupportRequestAsync("locked@example.com", "Please unlock my account.", "203.0.113.5", CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r.Type == NotificationTypeKeys.AccountSupportRequestSubmitted
            && r.Recipient is NotificationRecipient.SupportMailbox
            && r.Variables["requesterEmail"] == "locked@example.com"
            && r.Variables["messageBody"] == "Please unlock my account."));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
