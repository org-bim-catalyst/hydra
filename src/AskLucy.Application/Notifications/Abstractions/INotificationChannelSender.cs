using AskLucy.Domain.Notifications;

namespace AskLucy.Application.Notifications.Abstractions;

/// <summary>How a channel send ended (contracts/module-integration.md).</summary>
public enum ChannelSendOutcome
{
    /// <summary>The provider accepted the message.</summary>
    Sent,

    /// <summary>The provider confirmed receipt. Email never reports this; it has no delivery receipt.</summary>
    Delivered,

    /// <summary>Worth retrying on the schedule: a 4xx reply, a timeout, a dropped connection.</summary>
    TransientFailure,

    /// <summary>Retrying can't help: a 5xx reply, or a template that can't render.</summary>
    PermanentFailure,

    /// <summary>
    /// Nothing was sent and nothing went wrong: the channel's own send limiter has no capacity right
    /// now. The delivery goes back to the queue without using up an attempt.
    /// </summary>
    Deferred,
}

/// <summary>
/// How far one send got, reported by the channel sender itself: only it knows when the first byte is about
/// to leave the process. Before <see cref="MarkTransmissionStarted"/> nothing could have reached the
/// recipient, so a delivery interrupted by a host shutdown goes safely back to the queue; after it the
/// outcome is unknown, and the lease sweeper records the delivery as ambiguous rather than resend it (R5).
/// </summary>
public sealed class SendProgress
{
    public bool TransmissionStarted { get; private set; }

    /// <summary>Call immediately before handing the message to the provider.</summary>
    public void MarkTransmissionStarted() => TransmissionStarted = true;
}

/// <summary>
/// What a channel sender needs to send one delivery: everything is already resolved (recipient
/// address, language, variables, link), so a sender never reads the database for the message itself.
/// </summary>
/// <param name="DeliveryId">The delivery identity; it becomes the email <c>Message-ID</c> (R5).</param>
/// <param name="NotificationId">The notification this delivery belongs to.</param>
/// <param name="Definition">The catalogue row of the notification's type.</param>
/// <param name="Priority">The delivery's priority.</param>
/// <param name="Language">The language resolved at send time (FR-044).</param>
/// <param name="RecipientAddress">Where the message goes. Never logged by a sender.</param>
/// <param name="Variables">The event's declared variables plus the standard ones, including the absolute <c>actionUrl</c>.</param>
/// <param name="IsMandatory">Whether the type is mandatory on this channel; it draws from the reserved send lane.</param>
/// <param name="CorrelationId">Links the log lines of one event end to end.</param>
/// <param name="Progress">The sender calls <see cref="SendProgress.MarkTransmissionStarted"/> right before transmitting.</param>
public sealed record DeliveryContext(
    Guid DeliveryId,
    Guid NotificationId,
    NotificationTypeDefinition Definition,
    NotificationPriority Priority,
    string Language,
    string RecipientAddress,
    IReadOnlyDictionary<string, string?> Variables,
    bool IsMandatory,
    string CorrelationId,
    SendProgress? Progress = null);

/// <summary>
/// The outcome of one send attempt.
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="FailureKind">Set for a failure outcome.</param>
/// <param name="SafeReason">Human-readable and safe to store and show: never a credential, token, link or full server banner.</param>
/// <param name="ProviderResponse">The sanitized provider status line only (FR-056).</param>
/// <param name="TemplateVersionId">The published template version that was rendered, when one was.</param>
/// <param name="Language">The language actually rendered, which is <c>en</c> when the requested one had no published template.</param>
/// <param name="RequiresAttention">An authentication failure: transient for this delivery, but the channel needs an operator.</param>
/// <param name="RetryAfter">For <see cref="ChannelSendOutcome.Deferred"/>: how long until the channel has capacity.</param>
public sealed record ChannelSendResult(
    ChannelSendOutcome Outcome,
    DeliveryFailureKind? FailureKind,
    string? SafeReason,
    string? ProviderResponse,
    Guid? TemplateVersionId = null,
    string? Language = null,
    bool RequiresAttention = false,
    TimeSpan? RetryAfter = null)
{
    public static ChannelSendResult Deferred(TimeSpan retryAfter) =>
        new(ChannelSendOutcome.Deferred, null, null, null, RetryAfter: retryAfter);

    public static ChannelSendResult Sent(string? providerResponse, Guid templateVersionId, string language) =>
        new(ChannelSendOutcome.Sent, null, null, providerResponse, templateVersionId, language);

    public static ChannelSendResult Transient(string safeReason, string? providerResponse, bool requiresAttention = false) =>
        new(ChannelSendOutcome.TransientFailure, DeliveryFailureKind.Transient, safeReason, providerResponse, RequiresAttention: requiresAttention);

    public static ChannelSendResult Permanent(DeliveryFailureKind kind, string safeReason, string? providerResponse = null) =>
        new(ChannelSendOutcome.PermanentFailure, kind, safeReason, providerResponse);
}

/// <summary>
/// One delivery channel (FR-023, FR-060). A sender does not throw for a delivery problem: it
/// returns the classified result, and it raises <see cref="SendProgress.MarkTransmissionStarted"/> before
/// anything leaves the process. It lets an <see cref="OperationCanceledException"/> through only for a host
/// shutdown. The in-app channel has no sender, because it is delivered when the notification is materialized.
/// </summary>
public interface INotificationChannelSender
{
    NotificationChannel Channel { get; }

    Task<ChannelSendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken);
}
