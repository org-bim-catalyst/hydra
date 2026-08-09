using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Memory.Commands.MarkNotificationRead;

/// <summary>FR-027-equivalent scoping for notifications — reports not-found rather than confirming another user's notification exists, same posture as <c>MemoryOwnershipGuard</c>.</summary>
public sealed class MarkNotificationReadCommandHandler(
    IMemoryNotificationRepository notificationRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var notification = await notificationRepository.GetByIdAsync(request.NotificationId, cancellationToken);

        if (notification is null || notification.UserId != userId)
        {
            throw new KeyNotFoundException("Notification not found.");
        }

        notification.MarkRead(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
