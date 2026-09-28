using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Port for pushing processing status to the owning user in near-real-time, and for publishing
/// the document lifecycle/processing hub notifications (FR-027, FR-047, research.md Decision 7).
/// The <c>Web</c> project's SignalR hub is the concrete delivery mechanism for the near-real-time
/// pushes — Application/Domain never reference SignalR directly (constitution §3 Dependency Rule);
/// the <c>ProcessingNotifier</c> implementation lives in <c>Infrastructure</c> and is invoked via
/// a small port defined here. <see cref="NotifyAsync"/> and <see cref="NotifyOcrCompletedAsync"/>
/// (specs/067 T082) publish through <c>INotificationPublisher</c> instead of writing a
/// <c>DocumentNotification</c> row directly — the caller's own <c>IUnitOfWork.SaveChangesAsync</c>
/// commits the outbox event, so these two calls must run before that save, not after.
/// </summary>
public interface IProcessingNotifier
{
    Task NotifyStageChangedAsync(string userId, Guid documentId, DocumentProcessingStageType stageType, DocumentProcessingStageStatus status, CancellationToken cancellationToken = default);

    Task NotifyProcessingCompletedAsync(string userId, Guid documentId, CancellationToken cancellationToken = default);

    Task NotifyProcessingFailedAsync(string userId, Guid documentId, string failureReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps <paramref name="eventType"/> to its <c>document.*</c> catalogue key (specs/067
    /// data-model.md § Legacy mapping) and publishes it. <paramref name="documentId"/> is null
    /// only for <see cref="DocumentNotificationEventType.StorageLimitReached"/>, whose type has no
    /// related item (<c>RequiresItemAccess = false</c>). <paramref name="dedupeKey"/> becomes the
    /// last segment of the <c>document:{id}:{type}:{dedupeKey}</c> event key — the processing
    /// job id for processing/OCR events, the version number for a new version, or a coarse
    /// per-day key for the storage-limit warning, which has no document of its own.
    /// </summary>
    Task NotifyAsync(
        string userId,
        DocumentNotificationEventType eventType,
        Guid? documentId,
        string dedupeKey,
        string? documentName = null,
        string? failureSummary = null,
        string? versionNumber = null,
        string? usedStorage = null,
        string? storageLimit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes <c>document.ocr.completed</c> directly — this type has no legacy
    /// <see cref="DocumentNotificationEventType"/> predecessor, so it never goes through
    /// <see cref="NotifyAsync"/>'s legacy-mapped switch (T083).
    /// </summary>
    Task NotifyOcrCompletedAsync(string userId, Guid documentId, string documentName, string dedupeKey, CancellationToken cancellationToken = default);
}
