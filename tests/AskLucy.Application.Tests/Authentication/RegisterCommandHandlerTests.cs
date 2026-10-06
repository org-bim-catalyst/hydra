using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.Commands.Register;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// specs/067 US9-B (T125): registration asks the hub for the confirmation email instead of building and
/// sending it. The link, with its token, is minted when the email is sent, so nothing about it is in the
/// request, the outbox or this handler.
/// </summary>
public sealed class RegisterCommandHandlerTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterCommandHandler _handler;

    public RegisterCommandHandlerTests()
    {
        _handler = new RegisterCommandHandler(_identityService, _publisher, _unitOfWork);
    }

    [Fact]
    public async Task Handle_ShouldPublishTheConfirmationEmail_ToTheNewAddress_AndCommitIt()
    {
        _identityService.RegisterAsync("user@example.com", "Password1!", "Ada", "Lovelace", Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1"));

        var result = await _handler.Handle(
            new RegisterCommand("user@example.com", "Password1!", "Ada", "Lovelace"), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Success);
        result.UserId.Should().Be("user-1");
        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountEmailConfirmationRequested
                && r.Recipient == new NotificationRecipient.AddressForUser("user-1", "user@example.com")
                && r.Variables.Count == 0));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_ShouldNotAskForAConfirmationToken_BecauseTheLinkIsMintedWhenTheEmailIsSent()
    {
        _identityService.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Success, "user-1"));

        await _handler.Handle(new RegisterCommand("user@example.com", "Password1!", null, null), CancellationToken.None);

        await _identityService.DidNotReceiveWithAnyArgs().GenerateEmailConfirmationTokenAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Handle_ShouldReturnFailed_AndPublishNothing_WhenIdentityCreationFails()
    {
        _identityService.RegisterAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IdentityOperationResult(IdentityResultStatus.Failed, Errors: ["Email already taken"]));

        var result = await _handler.Handle(
            new RegisterCommand("dup@example.com", "Password1!", null, null), CancellationToken.None);

        result.Outcome.Should().Be(AuthOutcome.Failed);
        result.Errors.Should().Contain("Email already taken");
        _publisher.DidNotReceive().Publish(Arg.Any<NotificationRequest>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
