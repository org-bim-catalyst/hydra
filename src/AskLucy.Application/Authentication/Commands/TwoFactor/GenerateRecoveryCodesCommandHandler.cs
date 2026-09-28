using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using MediatR;
using INotificationPublisher = AskLucy.Application.Notifications.Abstractions.INotificationPublisher;

namespace AskLucy.Application.Authentication.Commands.TwoFactor;

public sealed class GenerateRecoveryCodesCommandHandler(
    IIdentityService identityService, INotificationPublisher notificationPublisher, IUnitOfWork unitOfWork)
    : IRequestHandler<GenerateRecoveryCodesCommand, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GenerateRecoveryCodesCommand request, CancellationToken cancellationToken)
    {
        var codes = await identityService.GenerateRecoveryCodesAsync(request.UserId, cancellationToken);

        var changedAtUtc = DateTime.UtcNow;
        notificationPublisher.Publish(new NotificationRequest(
            NotificationTypeKeys.SecurityRecoveryCodesRegenerated,
            new NotificationRecipient.User(request.UserId),
            new Dictionary<string, string?> { ["changedAt"] = $"{changedAtUtc:yyyy-MM-dd HH:mm} UTC" },
            EventKey: $"security:{request.UserId}:recovery-codes-regenerated:{changedAtUtc.Ticks}"));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return codes;
    }
}
