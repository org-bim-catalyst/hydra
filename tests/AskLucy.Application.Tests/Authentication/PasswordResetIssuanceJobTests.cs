#pragma warning disable CS0618 // The shim under test is obsolete by design: it exists for one release.

using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.PasswordReset;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// specs/067 US9-B (T133): <see cref="PasswordResetIssuanceJob"/> is a one-release forwarding shim. A Hangfire
/// job enqueued before the deploy still runs with its old signature, and now hands the request to the hub
/// instead of issuing a token itself; eligibility, throttling and token issuance live in the account link
/// issuer and run when the email is sent (see <c>AccountLinkIssuerTests</c>).
/// </summary>
public sealed class PasswordResetIssuanceJobTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task IssueAsync_ShouldPublishAPasswordResetRequestForTheAddress_AndCommitIt()
    {
        var job = new PasswordResetIssuanceJob(_publisher, _unitOfWork);

        await job.IssueAsync("  user@example.com ", "203.0.113.5", CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountPasswordResetRequested
                && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task IssueAsync_ShouldPublishTheSameWay_ForAnAddressWithNoAccount()
    {
        var job = new PasswordResetIssuanceJob(_publisher, _unitOfWork);

        await job.IssueAsync("nobody@example.invalid", null, CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null && r.Recipient is NotificationRecipient.AddressLookup));
    }
}
