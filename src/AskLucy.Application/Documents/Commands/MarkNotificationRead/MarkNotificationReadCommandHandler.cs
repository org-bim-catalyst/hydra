using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Documents.Commands.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler(
    IDocumentNotificationRepository notificationRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var notification = await notificationRepository.GetByIdAsync(request.NotificationId, userId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification not found.");

        notification.MarkRead(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
