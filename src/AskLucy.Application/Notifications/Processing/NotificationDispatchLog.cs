using AskLucy.Application.Notifications.Abstractions;
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
        Message = "Notification {Type} (EventKey {EventKey}, CorrelationId {CorrelationId}) names an address with no active account (address hash {AddressHash}); nothing was created.")]
    public static partial void AddressNotFound(ILogger logger, string type, string? eventKey, string correlationId, string addressHash);

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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Realtime notificationUpdated push failed (NotificationId {NotificationId}, Change {Change}); the client catches up on reconnect.")]
    public static partial void UpdatePushFailed(ILogger logger, Exception exception, Guid notificationId, NotificationChange change);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Realtime unreadCountChanged push failed for user {UserId}; the client catches up on reconnect.")]
    public static partial void UnreadCountPushFailed(ILogger logger, Exception exception, string userId);
}

internal static partial class DeliveryLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification retention removed {Read} read and {OwnerDeleted} deleted notifications, {Failed} failed and {Finished} finished deliveries, and {Outbox} completed outbox events.")]
    public static partial void RetentionRan(ILogger logger, int read, int ownerDeleted, int failed, int finished, int outbox);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification delivery {DeliveryId} is no longer leased to worker {WorkerId}; skipping it.")]
    public static partial void LeaseLost(ILogger logger, Guid deliveryId, string workerId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Notification {Type} (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}) has a type that is no longer catalogued; its delivery failed.")]
    public static partial void UnknownType(ILogger logger, string type, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) expired before it was sent.")]
    public static partial void Expired(ILogger logger, string type, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The outbox event for {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) is gone; rendering from each variable's fallback.")]
    public static partial void EventUnavailable(ILogger logger, string type, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) was cancelled: its one-time link was not issued.")]
    public static partial void LinkRefused(ILogger logger, string type, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "No sender is registered for the {Channel} channel (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}).")]
    public static partial void NoSender(ILogger logger, NotificationChannel channel, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The {Channel} channel sender threw instead of returning a result (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}); retrying on schedule.")]
    public static partial void SenderThrew(ILogger logger, Exception exception, NotificationChannel channel, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) failed permanently: {FailureKind}.")]
    public static partial void FailedPermanently(ILogger logger, string type, Guid deliveryId, string correlationId, DeliveryFailureKind failureKind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) failed on attempt {Attempt}; retrying at {NextAttemptAtUtc:O}.")]
    public static partial void RetryScheduled(ILogger logger, string type, Guid deliveryId, string correlationId, int attempt, DateTime nextAttemptAtUtc);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) was dead-lettered after {Attempts} attempts.")]
    public static partial void DeadLettered(ILogger logger, string type, Guid deliveryId, string correlationId, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The {Channel} channel needs operator attention: its credentials were rejected (DeliveryId {DeliveryId}, CorrelationId {CorrelationId}).")]
    public static partial void ChannelNeedsAttention(ILogger logger, NotificationChannel channel, Guid deliveryId, string correlationId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Notification {Type} delivery {DeliveryId} (CorrelationId {CorrelationId}) is now {Status} after {Attempts} attempt(s).")]
    public static partial void Processed(ILogger logger, string type, Guid deliveryId, string correlationId, DeliveryStatus status, int attempts);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Processing delivery {DeliveryId} failed (SendStarted {SendStarted}); {Action}.")]
    public static partial void ProcessFailed(ILogger logger, Exception exception, Guid deliveryId, bool sendStarted, string action);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Released {Count} claimed notification deliver(ies) held by {WorkerId} unsent.")]
    public static partial void Released(ILogger logger, int count, string workerId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Could not release {Count} claimed notification deliver(ies) held by {WorkerId}; they are recorded as ambiguous when their leases expire.")]
    public static partial void ReleaseFailed(ILogger logger, Exception exception, int count, string workerId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lease sweep: {Deliveries} delivery(ies) left ambiguous, {Events} outbox event(s) returned to pending.")]
    public static partial void Swept(ILogger logger, int deliveries, int events);
}
