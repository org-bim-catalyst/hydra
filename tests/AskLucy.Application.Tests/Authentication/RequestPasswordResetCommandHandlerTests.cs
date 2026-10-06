using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.RequestPasswordReset;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Authentication;

/// <summary>
/// specs/058-password-recovery T013, moved onto the hub by specs/067 US9-B (T125). Guards the one property
/// this handler exists for: it does no account-dependent work of its own, because anything it did on the
/// request thread would show up as a response-time difference between an address that has an account and
/// one that does not (FR-003, FR-009e). It publishes one request, the same one for every address, and the
/// hub decides in the background whether the address has an account.
/// </summary>
public sealed class RequestPasswordResetCommandHandlerTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeLogger<RequestPasswordResetCommandHandler> _logger = new();

    private RequestPasswordResetCommandHandler CreateSut() => new(_publisher, _unitOfWork, _logger);

    [Fact]
    public async Task Handle_ShouldPublishOneAddressLookupRequest_AndCommitIt_AndTouchNothingElse()
    {
        await CreateSut().Handle(new RequestPasswordResetCommand("user@example.com", "203.0.113.5"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _publisher.Publish(Arg.Is<NotificationRequest>(r => r != null &&
                r.Type == NotificationTypeKeys.AccountPasswordResetRequested
                && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")
                && r.Variables.Count == 0
                && r.EventKey == null));
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_ShouldPublishTheSameWay_ForAnAddressThatCannotPossiblyHaveAnAccount()
    {
        var sut = CreateSut();

        await sut.Handle(new RequestPasswordResetCommand("user@example.com", null), CancellationToken.None);
        await sut.Handle(new RequestPasswordResetCommand("nobody@example.invalid", null), CancellationToken.None);

        var requests = _publisher.ReceivedCalls().Select(c => (NotificationRequest)c.GetArguments()[0]!).ToList();
        requests.Should().HaveCount(2);
        requests.Select(r => (r.Type, r.Recipient.GetType(), r.Variables.Count, r.EventKey)).Distinct().Should().ContainSingle();
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldTrimTheAddressItPublishes()
    {
        await CreateSut().Handle(new RequestPasswordResetCommand("  user@example.com  ", null), CancellationToken.None);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null && r.Recipient == new NotificationRecipient.AddressLookup("user@example.com")));
    }

    [Fact]
    public async Task Handle_ShouldLogTheRequestWithItsOrigin_ButNeverTheAddress()
    {
        await CreateSut().Handle(new RequestPasswordResetCommand("user@example.com", "203.0.113.5"), CancellationToken.None);

        var entry = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject;
        entry.Message.Should().Contain("203.0.113.5").And.NotContain("user@example.com");
    }
}
