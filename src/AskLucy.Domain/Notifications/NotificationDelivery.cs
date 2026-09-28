using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// One channel's delivery stream for one notification (FR-012, FR-028; data-model.md). Its
/// <see cref="BaseEntity.Id"/> is the delivery identity and goes into the email <c>Message-ID</c>.
/// Child of <see cref="Notification"/>: created only through it, and reached only through its
/// repository (constitution §5).
/// </summary>
public sealed class NotificationDelivery : BaseEntity
{
    public const int FailureReasonMaxLength = 500;
    public const int ProviderResponseMaxLength = 500;
    public const int RecipientAddressMaxLength = 320;

    public Guid NotificationId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public NotificationPriority Priority { get; private set; }

    public RecipientKind RecipientKind { get; private set; }

    /// <summary>Explicit address for <see cref="RecipientKind.Address"/> only; never the support mailbox (FR-009c).</summary>
    public string? RecipientAddress { get; private set; }

    public string? Language { get; private set; }

    public Guid? TemplateVersionId { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public DeliverySkipReason? SkipReason { get; private set; }

    public DeliveryFailureKind? FailureKind { get; private set; }

    /// <summary>Safe and human-readable: never a token, link or credential.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The sanitized provider status line only (FR-056).</summary>
    public string? ProviderResponse { get; private set; }

    public DateTime? SentAtUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    /// <summary>Request validity (R10) or announcement end (R22).</summary>
    public DateTime? ExpiresAtUtc { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public bool IsTerminal => Status is DeliveryStatus.Sent or DeliveryStatus.Delivered or DeliveryStatus.Skipped
        or DeliveryStatus.Failed or DeliveryStatus.DeadLettered or DeliveryStatus.Cancelled or DeliveryStatus.Expired;

    private NotificationDelivery()
    {
        // Required by EF Core materialization.
    }

    /// <summary>The in-app delivery: the notification center is the in-app surface, so it is delivered on creation (R8).</summary>
    public static NotificationDelivery CreateDelivered(
        NotificationChannel channel,
        NotificationPriority priority,
        string? language,
        Guid? templateVersionId,
        string correlationId,
        DateTime now)
    {
        var d = New(channel, priority, RecipientKind.User, recipientAddress: null, maxAttempts: 1, expiresAtUtc: null, correlationId, now);
        d.Status = DeliveryStatus.Delivered;
        d.Language = language;
        d.TemplateVersionId = templateVersionId;
        d.DeliveredAtUtc = now;
        return d;
    }

    /// <summary>A delivery the worker will claim and send; due immediately.</summary>
    public static NotificationDelivery CreatePending(
        NotificationChannel channel,
        NotificationPriority priority,
        RecipientKind recipientKind,
        string? recipientAddress,
        int maxAttempts,
        DateTime? expiresAtUtc,
        string correlationId,
        DateTime now)
    {
        if (recipientKind == RecipientKind.Address && string.IsNullOrWhiteSpace(recipientAddress))
        {
            throw new DomainRuleViolationException("An address delivery needs an address.");
        }

        if (recipientKind != RecipientKind.Address && recipientAddress is not null)
        {
            throw new DomainRuleViolationException("Only an address delivery stores an address.");
        }

        if (maxAttempts < 1)
        {
            throw new DomainRuleViolationException("A delivery needs at least one attempt.");
        }

        var d = New(channel, priority, recipientKind, recipientAddress, maxAttempts, expiresAtUtc, correlationId, now);
        d.Status = DeliveryStatus.Pending;
        d.NextAttemptAtUtc = now;
        return d;
    }

    /// <summary>A channel the router left out, recorded so the admin view explains why (FR-003).</summary>
    public static NotificationDelivery CreateSkipped(
        NotificationChannel channel,
        NotificationPriority priority,
        RecipientKind recipientKind,
        DeliverySkipReason reason,
        string correlationId,
        DateTime now)
    {
        var d = New(channel, priority, recipientKind, recipientAddress: null, maxAttempts: 0, expiresAtUtc: null, correlationId, now);
        d.Status = DeliveryStatus.Skipped;
        d.SkipReason = reason;
        return d;
    }

    /// <summary>A delivery that failed before it could be queued, e.g. an in-app render error (T009).</summary>
    public static NotificationDelivery CreateFailed(
        NotificationChannel channel,
        NotificationPriority priority,
        RecipientKind recipientKind,
        DeliveryFailureKind kind,
        string safeReason,
        string correlationId,
        DateTime now)
    {
        var d = New(channel, priority, recipientKind, recipientAddress: null, maxAttempts: 0, expiresAtUtc: null, correlationId, now);
        d.Status = DeliveryStatus.Failed;
        d.FailureKind = kind;
        d.FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        d.LastAttemptAtUtc = now;
        return d;
    }

    /// <summary>A delivery for a recipient who is no longer active (data-model.md § Relationships).</summary>
    public static NotificationDelivery CreateCancelled(
        NotificationChannel channel,
        NotificationPriority priority,
        RecipientKind recipientKind,
        string safeReason,
        string correlationId,
        DateTime now)
    {
        var d = New(channel, priority, recipientKind, recipientAddress: null, maxAttempts: 0, expiresAtUtc: null, correlationId, now);
        d.Status = DeliveryStatus.Cancelled;
        d.FailureKind = DeliveryFailureKind.RecipientUnavailable;
        d.FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        return d;
    }

    internal void AttachTo(Guid notificationId) => NotificationId = notificationId;

    /// <summary>The claim (R4): due and unleased, or a <see cref="DeliveryStatus.Sending"/> row whose lease expired.</summary>
    public void MarkSending(string workerId, DateTime leaseExpiresAtUtc, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        if (Status is not (DeliveryStatus.Pending or DeliveryStatus.Retrying))
        {
            throw Invalid(nameof(MarkSending));
        }

        Status = DeliveryStatus.Sending;
        AttemptCount++;
        LastAttemptAtUtc = now;
        LeaseOwner = workerId;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    /// <summary>The provider accepted the message.</summary>
    public void MarkSent(string? language, Guid? templateVersionId, string? providerResponse, DateTime now)
    {
        RequireStatus(nameof(MarkSent), DeliveryStatus.Sending);
        Status = DeliveryStatus.Sent;
        Language = language;
        TemplateVersionId = templateVersionId;
        ProviderResponse = Truncate(providerResponse, ProviderResponseMaxLength);
        SentAtUtc = now;
        ClearLease();
        NextAttemptAtUtc = null;
    }

    /// <summary>A channel with delivery confirmation reports receipt. Email has none; in-app is created delivered.</summary>
    public void MarkDelivered(DateTime now)
    {
        RequireStatus(nameof(MarkDelivered), DeliveryStatus.Sending, DeliveryStatus.Sent);
        Status = DeliveryStatus.Delivered;
        DeliveredAtUtc = now;
        ClearLease();
    }

    public void Skip(DeliverySkipReason reason)
    {
        RequireStatus(nameof(Skip), DeliveryStatus.Pending);
        Status = DeliveryStatus.Skipped;
        SkipReason = reason;
        NextAttemptAtUtc = null;
    }

    /// <summary>
    /// A transient failure. Schedules the next attempt, or dead-letters the delivery once
    /// <see cref="MaxAttempts"/> is used up (R6).
    /// </summary>
    public void ScheduleRetry(DateTime nextAttemptAtUtc, DeliveryFailureKind kind, string safeReason, string? providerResponse, DateTime now)
    {
        RequireStatus(nameof(ScheduleRetry), DeliveryStatus.Sending);
        FailureKind = kind;
        FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        ProviderResponse = Truncate(providerResponse, ProviderResponseMaxLength);
        ClearLease();

        if (AttemptCount >= MaxAttempts)
        {
            Status = DeliveryStatus.DeadLettered;
            FailureKind = DeliveryFailureKind.RetryLimitReached;
            NextAttemptAtUtc = null;
            return;
        }

        Status = DeliveryStatus.Retrying;
        NextAttemptAtUtc = nextAttemptAtUtc < now ? now : nextAttemptAtUtc;
    }

    /// <summary>
    /// A failure that must not be retried automatically: permanent rejection, render error, or an
    /// <see cref="DeliveryFailureKind.AmbiguousOutcome"/> found by the lease sweeper (R5).
    /// </summary>
    public void Fail(DeliveryFailureKind kind, string safeReason, string? providerResponse, DateTime now)
    {
        RequireStatus(nameof(Fail), DeliveryStatus.Pending, DeliveryStatus.Retrying, DeliveryStatus.Sending);
        Status = DeliveryStatus.Failed;
        FailureKind = kind;
        FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        ProviderResponse = Truncate(providerResponse, ProviderResponseMaxLength);
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = null;
        ClearLease();
    }

    public void DeadLetter(string safeReason)
    {
        RequireStatus(nameof(DeadLetter), DeliveryStatus.Sending, DeliveryStatus.Retrying);
        Status = DeliveryStatus.DeadLettered;
        FailureKind = DeliveryFailureKind.RetryLimitReached;
        FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        NextAttemptAtUtc = null;
        ClearLease();
    }

    /// <summary>The recipient was deleted or deactivated before sending.</summary>
    public void Cancel(string safeReason)
    {
        RequireStatus(nameof(Cancel), DeliveryStatus.Pending, DeliveryStatus.Retrying);
        Status = DeliveryStatus.Cancelled;
        FailureKind = DeliveryFailureKind.RecipientUnavailable;
        FailureReason = Truncate(safeReason, FailureReasonMaxLength);
        NextAttemptAtUtc = null;
        ClearLease();
    }

    /// <summary>The request validity (R10) or the announcement end (R22) passed before a send.</summary>
    public void Expire()
    {
        RequireStatus(nameof(Expire), DeliveryStatus.Pending, DeliveryStatus.Retrying);
        Status = DeliveryStatus.Expired;
        FailureKind = DeliveryFailureKind.RequestExpired;
        NextAttemptAtUtc = null;
        ClearLease();
    }

    public bool IsExpiredAt(DateTime now) => ExpiresAtUtc is { } expires && expires <= now;

    /// <summary>
    /// A deliberate administrator retry (audited by the caller). Only from
    /// <see cref="DeliveryStatus.Failed"/> or <see cref="DeliveryStatus.DeadLettered"/>; the
    /// notification-level checks live in <see cref="Notification.RetryDelivery"/>.
    /// </summary>
    internal void ResetForRetry(DateTime now)
    {
        RequireStatus(nameof(ResetForRetry), DeliveryStatus.Failed, DeliveryStatus.DeadLettered);
        Status = DeliveryStatus.Pending;
        AttemptCount = 0;
        if (MaxAttempts < 1)
        {
            MaxAttempts = 1;
        }

        NextAttemptAtUtc = now;
        FailureKind = null;
        FailureReason = null;
        ProviderResponse = null;
        ClearLease();
    }

    private static NotificationDelivery New(
        NotificationChannel channel,
        NotificationPriority priority,
        RecipientKind recipientKind,
        string? recipientAddress,
        int maxAttempts,
        DateTime? expiresAtUtc,
        string correlationId,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (recipientAddress is { Length: > RecipientAddressMaxLength })
        {
            throw new DomainRuleViolationException($"A recipient address can't exceed {RecipientAddressMaxLength} characters.");
        }

        return new NotificationDelivery
        {
            Id = Guid.CreateVersion7(),
            Channel = channel,
            Priority = priority,
            RecipientKind = recipientKind,
            RecipientAddress = recipientAddress?.Trim(),
            MaxAttempts = maxAttempts,
            ExpiresAtUtc = expiresAtUtc,
            CorrelationId = correlationId,
            CreatedAtUtc = now,
        };
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private void RequireStatus(string operation, params DeliveryStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw Invalid(operation);
        }
    }

    private DomainRuleViolationException Invalid(string operation) =>
        new($"A {Status} delivery can't be changed by {operation}.");

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
