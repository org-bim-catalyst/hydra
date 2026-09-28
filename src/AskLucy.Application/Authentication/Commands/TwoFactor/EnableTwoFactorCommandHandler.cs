using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class EnableTwoFactorCommandHandler(
    IIdentityService identityService, INotificationPublisher notificationPublisher, IUnitOfWork unitOfWork)
    : IRequestHandler<EnableTwoFactorCommand, string>
{
    public async Task<string> Handle(EnableTwoFactorCommand request, CancellationToken cancellationToken)
    {
        var sharedKey = await identityService.EnableTwoFactorAsync(request.UserId, cancellationToken);

        var changedAtUtc = DateTime.UtcNow;
        notificationPublisher.Publish(new NotificationRequest(
            NotificationTypeKeys.SecurityTwoFactorEnabled,
            new NotificationRecipient.User(request.UserId),
            new Dictionary<string, string?> { ["changedAt"] = $"{changedAtUtc:yyyy-MM-dd HH:mm} UTC" },
            EventKey: $"security:{request.UserId}:two-factor-enabled:{changedAtUtc.Ticks}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return sharedKey;
    }
}
