using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Port for pushing processing status to the owning user in near-real-time and for creating/
/// delivering in-app notifications (FR-027, FR-047, research.md Decision 7). The <c>Web</c>
/// project's SignalR hub is the concrete delivery mechanism — Application/Domain never reference
/// SignalR directly (constitution §3 Dependency Rule); the <c>ProcessingNotifier</c>
/// implementation lives in <c>Infrastructure</c> and is invoked via a small port defined here.
/// </summary>
public interface IProcessingNotifier
{
    Task NotifyStageChangedAsync(string userId, Guid documentId, DocumentProcessingStageType stageType, DocumentProcessingStageStatus status, CancellationToken cancellationToken = default);

    Task NotifyProcessingCompletedAsync(string userId, Guid documentId, CancellationToken cancellationToken = default);

    Task NotifyProcessingFailedAsync(string userId, Guid documentId, string failureReason, CancellationToken cancellationToken = default);

    /// <summary>Creates a <see cref="DocumentNotification"/> row and pushes it over the same connection (FR-047's six event types).</summary>
    Task NotifyAsync(string userId, DocumentNotificationEventType eventType, Guid? documentId, string message, CancellationToken cancellationToken = default);
}
