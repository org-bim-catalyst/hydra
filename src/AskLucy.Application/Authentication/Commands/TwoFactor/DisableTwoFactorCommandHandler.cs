using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class DisableTwoFactorCommandHandler(
    IIdentityService identityService, INotificationPublisher notificationPublisher, IUnitOfWork unitOfWork)
    : IRequestHandler<DisableTwoFactorCommand>
{
    public async Task Handle(DisableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        await identityService.DisableTwoFactorAsync(request.UserId, cancellationToken);

        var changedAtUtc = DateTime.UtcNow;
        notificationPublisher.Publish(new NotificationRequest(
            NotificationTypeKeys.SecurityTwoFactorDisabled,
            new NotificationRecipient.User(request.UserId),
            new Dictionary<string, string?> { ["changedAt"] = $"{changedAtUtc:yyyy-MM-dd HH:mm} UTC" },
            EventKey: $"security:{request.UserId}:two-factor-disabled:{changedAtUtc.Ticks}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
