using AskLucy.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Notifications.Processing;

internal static partial class NotificationDispatchLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Dispatched {Type} notification event (EventKey {EventKey}, CorrelationId {CorrelationId}): {Outcome}, {CreatedCount} notification(s) created.")]
    public static partial void Dispatched(ILogger logger, string type, string? eventKey, string correlationId, OutboxEventOutcome outcome, int createdCount);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification outbox event {EventId} is no longer leased to worker {WorkerId}; skipping it.")]
    public static partial void LeaseLost(ILogger logger, Guid eventId, string workerId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Notification outbox event {EventId} has unknown type {Type} (EventKey {EventKey}, CorrelationId {CorrelationId}); rejected.")]
    public static partial void UnknownType(ILogger logger, string type, string? eventKey, string correlationId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification {Type} (EventKey {EventKey}, CorrelationId {CorrelationId}) names recipient {UserId}, who doesn't exist; skipped.")]
    public static partial void UnknownRecipient(ILogger logger, string type, string? eventKey, string correlationId, string userId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification {Type} (EventKey {EventKey}, CorrelationId {CorrelationId}) not created: recipient {UserId} can no longer access the related {RelatedItemType}.")]
    public static partial void AccessDenied(ILogger logger, string type, string? eventKey, string correlationId, string userId, string? relatedItemType);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "In-app template for {Type} could not be rendered (EventKey {EventKey}, CorrelationId {CorrelationId}, NotificationId {NotificationId}); the in-app delivery failed.")]
    public static partial void InAppRenderFailed(ILogger logger, Exception exception, string type, string? eventKey, string correlationId, Guid notificationId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Dispatching {Type} notification event {EventId} failed on attempt {Attempts} (EventKey {EventKey}, CorrelationId {CorrelationId}); releasing it with backoff.")]
    public static partial void DispatchFailed(ILogger logger, Exception exception, string type, string? eventKey, string correlationId, Guid eventId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Gave up dispatching {Type} notification event {EventId} (EventKey {EventKey}, CorrelationId {CorrelationId}) after the maximum attempts.")]
    public static partial void DispatchGaveUp(ILogger logger, string type, string? eventKey, string correlationId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Dispatching notification event {EventId} failed after worker {WorkerId} lost its lease; the new owner retries it.")]
    public static partial void DispatchFailedLeaseLost(ILogger logger, Exception exception, Guid eventId, string workerId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Could not release notification event {EventId} after a {FailureType}; it is reclaimed when its lease expires.")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, string failureType, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Realtime notificationCreated push failed (CorrelationId {CorrelationId}, NotificationId {NotificationId}); the client catches up on reconnect.")]
    public static partial void PushFailed(ILogger logger, Exception exception, string correlationId, Guid notificationId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Could not count unread notifications before pushing (CorrelationId {CorrelationId}); {Count} realtime push(es) skipped, and the client catches up on reconnect.")]
    public static partial void UnreadCountFailed(ILogger logger, Exception exception, string correlationId, int count);
}
