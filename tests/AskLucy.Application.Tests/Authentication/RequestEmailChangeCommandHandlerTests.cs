using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.ChangeEmail;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>specs/067 US9-B (T125): the change-email request goes to the hub, addressed to the new, unverified address.</summary>
public sealed class RequestEmailChangeCommandHandlerTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RequestEmailChangeCommandHandler _handler;

    public RequestEmailChangeCommandHandlerTests()
    {
        _handler = new RequestEmailChangeCommandHandler(_publisher, _unitOfWork);
    }

    [Fact]
    public async Task Handle_ShouldPublishTheChangeEmail_ToTheNewAddress_WithItMaskedForTheTemplate_AndCommitIt()
    {
        await _handler.Handle(new RequestEmailChangeCommand("user-1", "new.address@example.com"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountEmailChangeRequested
                && r.Recipient == new NotificationRecipient.AddressForUser("user-1", "new.address@example.com")
                && r.Variables["newEmailMasked"] == "n***@example.com"));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [InlineData("a@example.com", "a***@example.com")]
    [InlineData("layla@example.com", "l***@example.com")]
    [InlineData("x.y.z@sub.example.co.uk", "x***@sub.example.co.uk")]
    public void MaskAddress_KeepsTheFirstLetterAndTheDomain(string address, string expected) =>
        RequestEmailChangeCommandHandler.MaskAddress(address).Should().Be(expected);

    [Fact]
    public async Task Handle_ShouldNeverPutTheFullNewAddressInAVariable()
    {
        await _handler.Handle(new RequestEmailChangeCommand("user-1", "new.address@example.com"), CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null && r.Variables.Values.All(v => v != null && !v.Contains("new.address", StringComparison.Ordinal))));
    }
}
