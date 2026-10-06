using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.RequestAccountSupport;
using AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using FluentValidation.TestHelper;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// The two anonymous endpoints the sign-in page offers a stuck user, moved onto the hub by specs/067 US9-B
/// (T125). Both mirror <see cref="RequestPasswordResetCommandHandlerTests"/>: the handler must publish and
/// return, because any account lookup on the request thread would make the neutral 202 measurably slower
/// for an address that exists.
/// </summary>
public sealed class AccountRecoveryCommandHandlerTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task ResendEmailConfirmation_ShouldPublishAnAddressLookup_AndCommitIt()
    {
        var handler = new ResendEmailConfirmationCommandHandler(_publisher, _unitOfWork);

        await handler.Handle(new ResendEmailConfirmationCommand("user@example.com"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountEmailConfirmationRequested
                && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")
                && r.Variables.Count == 0));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ResendEmailConfirmation_ShouldPublishTheSameWay_ForAnAddressThatCannotHaveAnAccount()
    {
        var handler = new ResendEmailConfirmationCommandHandler(_publisher, _unitOfWork);

        await handler.Handle(new ResendEmailConfirmationCommand("user@example.com"), CancellationToken.None);
        await handler.Handle(new ResendEmailConfirmationCommand("nobody@example.invalid"), CancellationToken.None);

        var requests = _publisher.ReceivedCalls().Select(c => (NotificationRequest)c.GetArguments()[0]!).ToList();
        requests.Select(r => (r.Type, r.Recipient.GetType(), r.Variables.Count)).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task RequestAccountSupport_ShouldPublishToTheSupportMailbox_WithTheSenderAndTheirMessage()
    {
        var handler = new RequestAccountSupportCommandHandler(_publisher, _unitOfWork);

        await handler.Handle(
            new RequestAccountSupportCommand("locked@example.com", "Please unlock my account.", "203.0.113.5"),
            CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountSupportRequestSubmitted
                && r.Recipient is NotificationRecipient.SupportMailbox
                && r.Variables["requesterEmail"] == "locked@example.com"
                && r.Variables["messageBody"] == "Please unlock my account."
                && r.Variables["requestKind"] == "access"));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RequestAccountSupport_ShouldNeverCarryASupportAddress_BecauseTheMailboxComesFromServerConfigurationAtSendTime()
    {
        var handler = new RequestAccountSupportCommandHandler(_publisher, _unitOfWork);

        await handler.Handle(new RequestAccountSupportCommand("locked@example.com", "Hi", null), CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r.Recipient.GetType() == typeof(NotificationRecipient.SupportMailbox)
            && r.Variables.Values.All(v => v == null || !v.Contains("support@", StringComparison.OrdinalIgnoreCase))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    public void ResendEmailConfirmationValidator_ShouldRejectAnUnusableAddress(string email)
    {
        var result = new ResendEmailConfirmationCommandValidator().TestValidate(new ResendEmailConfirmationCommand(email));

        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void RequestAccountSupportValidator_ShouldRejectAMessageBeyondTheRelayBound()
    {
        var command = new RequestAccountSupportCommand("locked@example.com", new string('x', 2001), null);

        var result = new RequestAccountSupportCommandValidator().TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.Message);
    }

    [Fact]
    public void RequestAccountSupportValidator_ShouldAcceptAMessageAtTheBound()
    {
        var command = new RequestAccountSupportCommand("locked@example.com", new string('x', 2000), null);

        new RequestAccountSupportCommandValidator().TestValidate(command).IsValid.Should().BeTrue();
    }
}
