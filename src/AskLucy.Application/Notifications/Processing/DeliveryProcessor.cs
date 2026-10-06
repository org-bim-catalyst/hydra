using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.OperationalFailures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Notifications.Processing;

/// <summary>How one claimed delivery was left.</summary>
public enum DeliveryProcessOutcome
{
    /// <summary>It reached a state it will not leave without a new decision, or it is waiting for its next retry.</summary>
    Completed,

    /// <summary>The worker no longer held the lease, so someone else owns the delivery now.</summary>
    LeaseLost,

    /// <summary>The channel had no send capacity: the claim was handed back unspent.</summary>
    Deferred,

    /// <summary>An unexpected failure; the service logs it and decides whether the claim can be released.</summary>
    Failed,
}

/// <summary>
/// Sends one claimed delivery (specs/067 US3, research R5, R6): re-validates the recipient and the
/// expiry, resolves the language and the link at send time, hands the delivery to its channel sender,
/// and records the classified result with the retry schedule, then recomputes the notification's status
/// and saves. Scoped: the batch service gives each delivery its own scope, so one delivery's failure
/// leaves nothing tracked for the next.
/// </summary>
public sealed class DeliveryProcessor(
    INotificationRepository notifications,
    INotificationOutboxStore outbox,
    INotificationRecipientDirectory directory,
    IEffectiveLanguageResolver languages,
    INotificationLinkBuilder links,
    IAccountLinkIssuer linkIssuer,
    ISupportMailboxResolver supportMailbox,
    IEnumerable<INotificationChannelSender> senders,
    INotificationMetrics metrics,
    IOperationalFailureRecorder failureRecorder,
    INotificationAuditWriter audit,
    IUnitOfWork unitOfWork,
    IOptions<NotificationsOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryProcessor> logger)
{
    private static readonly JsonSerializerOptions VariablesJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// How long recording a finished send may take after a host shutdown began. Once the provider has accepted
    /// (or refused) a message, the outcome is known, and losing it to the shutdown would leave a delivered email
    /// recorded as ambiguous; but a hung database must not hold the shutdown for ever.
    /// </summary>
    private static readonly TimeSpan RecordingGrace = TimeSpan.FromSeconds(10);

    private const string RecipientGoneReason = "The recipient account is no longer active.";
    private const string NoVerifiedAddressReason = "The recipient has no verified email address.";
    private const string DeletedReason = "The notification was deleted before it was sent.";

    private readonly SendProgress _progress = new();

    /// <summary>
    /// True once the channel sender reported that transmission was starting (or returned). Before that
    /// nothing could have reached the recipient, so an interrupted delivery is safe to give back; after it
    /// the outcome is unknown and the lease sweeper records it as ambiguous (R5).
    /// </summary>
    public bool SendStarted => _progress.TransmissionStarted;

    /// <summary>For <see cref="DeliveryProcessOutcome.Deferred"/>: how long until the channel has capacity.</summary>
    public TimeSpan? RetryAfter { get; private set; }

    /// <summary>Throws only for a host shutdown or an unexpected failure; the caller logs it, and <see cref="SendStarted"/> says whether the claim may be released.</summary>
    public async Task<DeliveryProcessOutcome> ProcessAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetClaimedDeliveryAsync(deliveryId, workerId, cancellationToken);
        if (notification is null)
        {
            DeliveryLog.LeaseLost(logger, deliveryId, workerId);
            return DeliveryProcessOutcome.LeaseLost;
        }

        var delivery = notification.Deliveries.Single(d => d.Id == deliveryId);
        var now = Now();

        if (!NotificationTypeCatalog.TryGet(notification.Type, out var definition) || definition is null)
        {
            // Only possible if a type was removed while its deliveries were queued.
            DeliveryLog.UnknownType(logger, notification.Type, deliveryId, delivery.CorrelationId);
            delivery.Fail(DeliveryFailureKind.RenderError, "The notification type no longer exists.", null, now);
            return await FinishAsync(notification, delivery, definition: null, cancellationToken);
        }

        if (notification.DeletedAtUtc is not null)
        {
            delivery.Cancel(DeletedReason);
            return await FinishAsync(notification, delivery, definition, cancellationToken);
        }

        if (delivery.IsExpiredAt(now) || notification.ExpiresAtUtc is { } expires && expires <= now)
        {
            delivery.Expire();
            DeliveryLog.Expired(logger, definition.Key, deliveryId, delivery.CorrelationId);
            return await FinishAsync(notification, delivery, definition, cancellationToken);
        }

        var recipient = await ResolveRecipientAsync(notification, delivery, cancellationToken);
        if (recipient.Address is null)
        {
            ApplyRecipientRefusal(delivery, recipient, now);
            return await FinishAsync(notification, delivery, definition, cancellationToken);
        }

        var outboxEvent = notification.SourceEventId is { } eventId ? await outbox.FindAsync(eventId, cancellationToken) : null;
        if (outboxEvent is null)
        {
            // The event was purged (retention) or never existed: the email still renders, from each
            // variable's fallback, rather than never being sent.
            DeliveryLog.EventUnavailable(logger, definition.Key, deliveryId, delivery.CorrelationId);
        }

        var language = await languages.ResolveAsync(notification.RecipientUserId, outboxEvent?.ExplicitLanguage, cancellationToken);

        string? actionUrl;
        try
        {
            actionUrl = await ActionUrlAsync(definition, notification, delivery, outboxEvent, recipient.Address, cancellationToken);
        }
        catch (AccountLinkRefusedException refusal)
        {
            // The real reason is in the security log; the delivery records only the neutral one.
            delivery.Cancel(refusal.Message);
            DeliveryLog.LinkRefused(logger, definition.Key, deliveryId, delivery.CorrelationId);
            return await FinishAsync(notification, delivery, definition, cancellationToken);
        }

        var variables = NotificationStandardVariables.Build(
            DeclaredVariablesOf(outboxEvent), recipient.DisplayName, actionUrl, outboxEvent?.OccurredAtUtc ?? notification.CreatedAtUtc);

        var sender = senders.FirstOrDefault(s => s.Channel == delivery.Channel);
        if (sender is null)
        {
            // Can't happen while the registry only lists channels that have a sender; fail visibly if it does.
            DeliveryLog.NoSender(logger, delivery.Channel, deliveryId, delivery.CorrelationId);
            delivery.Fail(DeliveryFailureKind.Permanent, $"No sender is registered for the {delivery.Channel} channel.", null, now);
            return await FinishAsync(notification, delivery, definition, cancellationToken);
        }

        var context = new DeliveryContext(
            delivery.Id,
            notification.Id,
            definition,
            delivery.Priority,
            language,
            recipient.Address,
            variables,
            definition.IsMandatory(delivery.Channel),
            delivery.CorrelationId,
            _progress);

        ChannelSendResult result;
        try
        {
            result = await sender.SendAsync(context, cancellationToken);

            // Whatever it returned, the sender is done with the provider: anything after this point that
            // goes wrong (saving the result) must not release the delivery, or it would be sent again.
            _progress.MarkTransmissionStarted();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A sender is meant to return its failures; one that throws has a bug. The delivery is retried
            // on schedule rather than lost, and the bug is logged with its correlation id.
            DeliveryLog.SenderThrew(logger, ex, delivery.Channel, deliveryId, delivery.CorrelationId);
            _progress.MarkTransmissionStarted();
            result = ChannelSendResult.Transient("The channel failed unexpectedly while sending.", null);
        }

        // The send is over and its outcome known: record it even if the host began to stop meanwhile.
        using var recording = new CancellationTokenSource(RecordingGrace);
        return await ApplyResultAsync(notification, delivery, definition, result, recording.Token);
    }

    /// <summary>
    /// Called, in a fresh scope, after <see cref="ProcessAsync"/> threw before the delivery reached its channel
    /// sender (a database error, a lookup failing). Nothing was sent, but the attempt counts and the retry
    /// schedule applies, exactly as for a transient send failure: otherwise a delivery that always fails to
    /// prepare would be claimed again every second for ever and never reach a dead letter, where an
    /// administrator can see it. If this itself fails, the lease expires and the sweeper takes over.
    /// </summary>
    public async Task RecordPreparationFailureAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken)
    {
        var notification = await notifications.GetClaimedDeliveryAsync(deliveryId, workerId, cancellationToken);
        if (notification is null)
        {
            DeliveryLog.LeaseLost(logger, deliveryId, workerId);
            return;
        }

        var delivery = notification.Deliveries.Single(d => d.Id == deliveryId);
        ScheduleRetry(
            delivery,
            notification.Type,
            ChannelSendResult.Transient("The delivery could not be prepared for sending, so it will be tried again.", null),
            Now());
        await FinishAsync(notification, delivery, definition: null, cancellationToken);
    }

    private async Task<DeliveryProcessOutcome> ApplyResultAsync(
        Notification notification,
        NotificationDelivery delivery,
        NotificationTypeDefinition definition,
        ChannelSendResult result,
        CancellationToken cancellationToken)
    {
        var now = Now();
        switch (result.Outcome)
        {
            case ChannelSendOutcome.Sent or ChannelSendOutcome.Delivered:
                delivery.MarkSent(result.Language, result.TemplateVersionId, result.ProviderResponse, now);
                if (result.Outcome == ChannelSendOutcome.Delivered)
                {
                    delivery.MarkDelivered(now);
                }

                metrics.DeliverySent(delivery.Channel, now - delivery.CreatedAtUtc);

                // FR-054: an approval request's email being handed to the provider is part of its audit trail. It is
                // saved with the delivery's own result below, so the two can't disagree.
                if (definition.IsApproval && delivery.Channel == NotificationChannel.Email)
                {
                    audit.Write(
                        NotificationAuditAction.ApprovalNotificationDelivered,
                        nameof(Notification),
                        notification.Id.ToString(),
                        NotificationAuditOutcome.Succeeded,
                        new { type = definition.Key, channel = delivery.Channel.ToString() },
                        notification.CorrelationId);
                }

                break;

            case ChannelSendOutcome.Deferred:
                RetryAfter = result.RetryAfter ?? TimeSpan.FromSeconds(5);
                delivery.Defer(now + RetryAfter.Value, now);
                notification.RecomputeStatus();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return DeliveryProcessOutcome.Deferred;

            case ChannelSendOutcome.TransientFailure:
                ScheduleRetry(delivery, definition.Key, result, now);
                break;

            default:
                delivery.Fail(result.FailureKind ?? DeliveryFailureKind.Permanent, result.SafeReason ?? "The delivery failed permanently.", result.ProviderResponse, now);
                metrics.DeliveryFailed(delivery.Channel, delivery.FailureKind ?? DeliveryFailureKind.Permanent);
                if (delivery.FailureKind != DeliveryFailureKind.RenderError)
                {
                    metrics.ProviderError(delivery.Channel, delivery.FailureKind ?? DeliveryFailureKind.Permanent);
                }

                DeliveryLog.FailedPermanently(logger, definition.Key, delivery.Id, delivery.CorrelationId, delivery.FailureKind ?? DeliveryFailureKind.Permanent);
                break;
        }

        return await FinishAsync(notification, delivery, definition, cancellationToken);
    }

    /// <summary>
    /// Attempt n waits the n-th delay and the last delay repeats (R6). Critical priority has its own,
    /// shorter schedule in seconds. The domain turns the last attempt into a dead letter.
    /// </summary>
    private void ScheduleRetry(NotificationDelivery delivery, string type, ChannelSendResult result, DateTime now)
    {
        var next = now + RetryDelay(delivery);
        delivery.ScheduleRetry(next, DeliveryFailureKind.Transient, result.SafeReason ?? "The delivery failed temporarily.", result.ProviderResponse, now);
        metrics.ProviderError(delivery.Channel, DeliveryFailureKind.Transient);

        if (result.RequiresAttention)
        {
            DeliveryLog.ChannelNeedsAttention(logger, delivery.Channel, delivery.Id, delivery.CorrelationId);
            failureRecorder.Record(new OperationalFailureReport
            {
                Engine = OperationalFailureEngine.BackgroundJob,
                Operation = $"Notification {delivery.Channel} delivery",
                Kind = OperationalFailureKind.CredentialRejected,
                Reason = $"The {delivery.Channel} channel's credentials were rejected, so notifications can't be sent until they are fixed.",
                CorrelationId = delivery.CorrelationId,
                OccurredAtUtc = now,
            });
        }

        if (delivery.Status == DeliveryStatus.DeadLettered)
        {
            metrics.DeliveryDeadLettered(delivery.Channel);
            metrics.DeliveryFailed(delivery.Channel, DeliveryFailureKind.RetryLimitReached);
            DeliveryLog.DeadLettered(logger, type, delivery.Id, delivery.CorrelationId, delivery.AttemptCount);
            failureRecorder.Record(new OperationalFailureReport
            {
                Engine = OperationalFailureEngine.BackgroundJob,
                Operation = "Notification delivery",
                Kind = OperationalFailureKind.JobFailedAfterRetries,
                Reason = $"A {type} {delivery.Channel} notification could not be delivered after {delivery.AttemptCount} attempts.",
                CorrelationId = delivery.CorrelationId,
                OccurredAtUtc = now,
            });
        }
        else
        {
            metrics.DeliveryRetried(delivery.Channel);
            DeliveryLog.RetryScheduled(logger, type, delivery.Id, delivery.CorrelationId, delivery.AttemptCount, next);
        }
    }

    private TimeSpan RetryDelay(NotificationDelivery delivery)
    {
        var retry = options.Value.Retry;
        var index = Math.Max(0, delivery.AttemptCount - 1);
        if (delivery.Priority == NotificationPriority.Critical)
        {
            var seconds = retry.EffectiveCriticalDelaysSeconds;
            return TimeSpan.FromSeconds(seconds[Math.Min(index, seconds.Count - 1)]);
        }

        var minutes = retry.EffectiveDelaysMinutes;
        return TimeSpan.FromMinutes(minutes[Math.Min(index, minutes.Count - 1)]);
    }

    /// <summary>Recomputes the aggregate status and commits the delivery's new state in one save.</summary>
    private async Task<DeliveryProcessOutcome> FinishAsync(
        Notification notification,
        NotificationDelivery delivery,
        NotificationTypeDefinition? definition,
        CancellationToken cancellationToken)
    {
        notification.RecomputeStatus();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        DeliveryLog.Processed(logger, definition?.Key ?? notification.Type, delivery.Id, delivery.CorrelationId, delivery.Status, delivery.AttemptCount);
        return DeliveryProcessOutcome.Completed;
    }

    private static void ApplyRecipientRefusal(NotificationDelivery delivery, RecipientResolution recipient, DateTime now)
    {
        if (recipient.Failure is { } failure)
        {
            delivery.Fail(DeliveryFailureKind.Permanent, failure, null, now);
            return;
        }

        delivery.Cancel(recipient.CancelReason ?? RecipientGoneReason);
    }

    private async Task<RecipientResolution> ResolveRecipientAsync(
        Notification notification, NotificationDelivery delivery, CancellationToken cancellationToken)
    {
        NotificationRecipientInfo? account = null;
        if (notification.RecipientUserId is { } userId)
        {
            var accounts = await directory.GetAsync([userId], cancellationToken);
            if (!accounts.TryGetValue(userId, out account) || !account.IsActive)
            {
                return RecipientResolution.Cancelled(RecipientGoneReason);
            }
        }

        switch (delivery.RecipientKind)
        {
            case RecipientKind.User:
                return account is { Email: { Length: > 0 } email, EmailConfirmed: true }
                    ? new RecipientResolution(email, account.DisplayName)
                    : RecipientResolution.Cancelled(NoVerifiedAddressReason);

            case RecipientKind.Address:
                // The address being confirmed or changed to. A user greeting falls back to that address.
                return string.IsNullOrWhiteSpace(delivery.RecipientAddress)
                    ? RecipientResolution.Cancelled(NoVerifiedAddressReason)
                    : new RecipientResolution(delivery.RecipientAddress, account?.DisplayName ?? delivery.RecipientAddress);

            default:
                // The support mailbox comes from server configuration at send time only (FR-009c).
                var mailbox = supportMailbox.GetAddress();
                return string.IsNullOrWhiteSpace(mailbox)
                    ? RecipientResolution.Failed("No support mailbox is configured.")
                    : new RecipientResolution(mailbox, null);
        }
    }

    /// <summary>
    /// An account email's one-time link is minted now and used only to render (R10). Every other type
    /// gets its absolute route, or nothing when it has none.
    /// </summary>
    private async Task<string?> ActionUrlAsync(
        NotificationTypeDefinition definition,
        Notification notification,
        NotificationDelivery delivery,
        NotificationOutboxEvent? outboxEvent,
        string recipientAddress,
        CancellationToken cancellationToken)
    {
        if (definition.SensitiveLinkKind is { } kind)
        {
            var userId = notification.RecipientUserId
                ?? throw new InvalidOperationException($"'{definition.Key}' needs a recipient user to mint its link for.");
            var link = await linkIssuer.IssueAsync(kind, userId, recipientAddress, isRetry: delivery.AttemptCount > 1, cancellationToken);
            return link.AbsoluteUri;
        }

        var relatedItem = notification is { RelatedItemType: { } type, RelatedItemId: { } id }
            ? new RelatedItem(type, id, outboxEvent?.RelatedItemParentId)
            : null;
        return links.BuildAbsolute(definition, relatedItem, notification.Id, DeclaredVariablesOf(outboxEvent));
    }

    private static Dictionary<string, string?> DeclaredVariablesOf(NotificationOutboxEvent? outboxEvent) =>
        outboxEvent is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(outboxEvent.VariablesJson, VariablesJson) ?? [];

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record RecipientResolution(string? Address, string? DisplayName, string? CancelReason = null, string? Failure = null)
    {
        public static RecipientResolution Cancelled(string reason) => new(null, null, reason);

        public static RecipientResolution Failed(string reason) => new(null, null, Failure: reason);
    }
}
