using AskLucy.Application.Abstractions;
using AskLucy.Domain.Memory;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Memory;

/// <summary>
/// <see cref="IMemoryNotifier"/> implementation — persists a <see cref="MemoryNotification"/> row
/// then pushes it over <see cref="MemoryHub"/> (spec.md FR-006a, research.md Decision 11), mirroring
/// <c>ProcessingNotifier</c>'s persist-then-push idiom exactly.
/// </summary>
public sealed class MemoryNotifier(
    IHubContext<MemoryHub> hubContext,
    IMemoryNotificationRepository notificationRepository,
    IUnitOfWork unitOfWork) : IMemoryNotifier
{
    private const string SystemActor = "system:memory";

    public async Task NotifyAsync(string userId, Guid? memoryId, MemoryNotificationEventType eventType, string message, CancellationToken cancellationToken = default)
    {
        var notification = MemoryNotification.Create(userId, memoryId, eventType, message, SystemActor);
        notificationRepository.Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await hubContext.Clients.Group(MemoryHub.UserGroup(userId))
            .SendAsync("memoryNotificationCreated", new
            {
                id = notification.Id,
                memoryId,
                eventType = eventType.ToString(),
                message,
                createdAtUtc = notification.CreatedAtUtc,
            }, cancellationToken);
    }
}
