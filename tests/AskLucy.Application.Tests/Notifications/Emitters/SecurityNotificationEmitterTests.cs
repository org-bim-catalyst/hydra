using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.TwoFactor;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications.Emitters;

/// <summary>T078 (specs/067) — the three TwoFactor handlers each publish their <c>security.*</c> type once, before their own save.</summary>
public sealed class SecurityNotificationEmitterTests
{
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly INotificationPublisher _notificationPublisher = Substitute.For<INotificationPublisher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task EnableTwoFactor_ShouldPublishSecurityTwoFactorEnabled_OnceBeforeSave()
    {
        _identityService.EnableTwoFactorAsync("user-1", Arg.Any<CancellationToken>()).Returns("shared-key");
        var sut = new EnableTwoFactorCommandHandler(_identityService, _notificationPublisher, _unitOfWork);

        await sut.Handle(new EnableTwoFactorCommand("user-1"), TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.SecurityTwoFactorEnabled &&
            ((NotificationRecipient.User)r.Recipient).UserId == "user-1" &&
            r.EventKey!.StartsWith("security:user-1:two-factor-enabled:")));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisableTwoFactor_ShouldPublishSecurityTwoFactorDisabled_Once()
    {
        var sut = new DisableTwoFactorCommandHandler(_identityService, _notificationPublisher, _unitOfWork);

        await sut.Handle(new DisableTwoFactorCommand("user-2"), TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.SecurityTwoFactorDisabled &&
            ((NotificationRecipient.User)r.Recipient).UserId == "user-2" &&
            r.EventKey!.StartsWith("security:user-2:two-factor-disabled:")));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateRecoveryCodes_ShouldPublishSecurityRecoveryCodesRegenerated_Once()
    {
        _identityService.GenerateRecoveryCodesAsync("user-3", Arg.Any<CancellationToken>()).Returns(["code-1"]);
        var sut = new GenerateRecoveryCodesCommandHandler(_identityService, _notificationPublisher, _unitOfWork);

        await sut.Handle(new GenerateRecoveryCodesCommand("user-3"), TestContext.Current.CancellationToken);

        _notificationPublisher.Received(1).Publish(Arg.Is<NotificationRequest>(r => r != null &&
            r!.Type == NotificationTypeKeys.SecurityRecoveryCodesRegenerated &&
            ((NotificationRecipient.User)r.Recipient).UserId == "user-3" &&
            r.EventKey!.StartsWith("security:user-3:recovery-codes-regenerated:")));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
