using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Legacy;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Documents;

/// <summary>
/// <see cref="IProcessingNotifier"/> implementation — pushes the near-real-time stage/progress
/// events over <see cref="DocumentProcessingHub"/> unchanged, and publishes the hub notification
/// types through <see cref="INotificationPublisher"/> (specs/067 T082). It no longer writes a
/// <see cref="DocumentNotification"/> row or pushes <c>notificationCreated</c> — the notification
/// hub (dispatcher/materializer) owns delivery from here on.
/// </summary>
public sealed class ProcessingNotifier(
    IHubContext<DocumentProcessingHub> hubContext,
    INotificationPublisher publisher) : IProcessingNotifier
{
    public Task NotifyStageChangedAsync(string userId, Guid documentId, DocumentProcessingStageType stageType, DocumentProcessingStageStatus status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentStageChanged", new { documentId, stageType = stageType.ToString(), status = status.ToString() }, cancellationToken);

    public Task NotifyProcessingCompletedAsync(string userId, Guid documentId, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentProcessingCompleted", new { documentId }, cancellationToken);

    public Task NotifyProcessingFailedAsync(string userId, Guid documentId, string failureReason, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(DocumentProcessingHub.UserGroup(userId))
            .SendAsync("documentProcessingFailed", new { documentId, failureReason }, cancellationToken);

    public Task NotifyAsync(
        string userId,
        DocumentNotificationEventType eventType,
        Guid? documentId,
        string dedupeKey,
        string? documentName = null,
        string? failureSummary = null,
        string? versionNumber = null,
        string? usedStorage = null,
        string? storageLimit = null,
        CancellationToken cancellationToken = default)
    {
        var catalogKey = LegacyNotificationTypeMap.ToCatalogKey(eventType);

        var variables = eventType switch
        {
            DocumentNotificationEventType.StorageLimitReached => new Dictionary<string, string?>
            {
                ["usedStorage"] = usedStorage,
                ["storageLimit"] = storageLimit,
            },
            DocumentNotificationEventType.VersionCreated => new Dictionary<string, string?>
            {
                ["documentName"] = documentName,
                ["versionNumber"] = versionNumber,
            },
            DocumentNotificationEventType.ProcessingFailed or DocumentNotificationEventType.OcrFailed => new Dictionary<string, string?>
            {
                ["documentName"] = documentName,
                ["failureSummary"] = failureSummary,
            },
            _ => new Dictionary<string, string?> { ["documentName"] = documentName },
        };

        publisher.Publish(new NotificationRequest(
            catalogKey,
            new NotificationRecipient.User(userId),
            variables,
            documentId is { } id ? new RelatedItem("Document", id.ToString()) : null,
            EventKey: $"document:{documentId?.ToString() ?? userId}:{eventType}:{dedupeKey}"));

        return Task.CompletedTask;
    }

    public Task NotifyOcrCompletedAsync(string userId, Guid documentId, string documentName, string dedupeKey, CancellationToken cancellationToken = default)
    {
        publisher.Publish(new NotificationRequest(
            NotificationTypeKeys.DocumentOcrCompleted,
            new NotificationRecipient.User(userId),
            new Dictionary<string, string?> { ["documentName"] = documentName },
            new RelatedItem("Document", documentId.ToString()),
            EventKey: $"document:{documentId}:ocr-completed:{dedupeKey}"));

        return Task.CompletedTask;
    }
}
