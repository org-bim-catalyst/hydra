using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Documents;

/// <summary>
/// <see cref="IProcessingNotifier"/> implementation — pushes over <see cref="DocumentProcessingHub"/>
/// and persists <see cref="DocumentNotification"/> rows (FR-027, FR-047, research.md Decision 7).
/// The actor for created rows is always <c>"system:processing"</c> — these events originate from
/// the background pipeline, not an interactive user request with its own <c>ICurrentUserAccessor</c>
/// context.
/// </summary>
public sealed class ProcessingNotifier(
    IHubContext<DocumentProcessingHub> hubContext,
    IDocumentNotificationRepository notificationRepository,
    IUnitOfWork unitOfWork) : IProcessingNotifier
{
    private const string SystemActor = "system:processing";

    public Task NotifyStageChangedAsync(string userId, Guid documentId, DocumentProcessingStageType stageType, DocumentProcessingStageStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentStageChanged", new { documentId, stageType = stageType.ToString(), status = status.ToString() }, cancellationToken);

    public Task NotifyProcessingCompletedAsync(string userId, Guid documentId, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentProcessingCompleted", new { documentId }, cancellationToken);

    public Task NotifyProcessingFailedAsync(string userId, Guid documentId, string failureReason, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentProcessingFailed", new { documentId, failureReason }, cancellationToken);

    public async Task NotifyAsync(string userId, DocumentNotificationEventType eventType, Guid? documentId, string message, CancellationToken cancellationToken = default)
    {
        var notification = DocumentNotification.Create(userId, documentId, eventType, message, SystemActor);
        notificationRepository.Add(notification);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("notificationCreated", new
            {
                id = notification.Id,
                documentId,
                eventType = eventType.ToString(),
                message,
                createdAtUtc = notification.CreatedAtUtc,
            }, cancellationToken);
    }
}
