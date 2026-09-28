using System.Text;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>How a completed outbox event ended (data-model.md).</summary>
public enum OutboxEventOutcome
{
    Materialized,
    NoRecipient,
    Duplicate,
    Rejected,
}

/// <summary>
/// A durable "something happened" record, written in the emitter's own transaction (FR-006,
/// research R2, ADR 0018). This is the spec's <i>Notification Event</i>. It carries no display
/// data: recipients are ids or an address kind, and variables are the type's declared ones only.
/// </summary>
public sealed class NotificationOutboxEvent : BaseEntity
{
    public const int TypeMaxLength = 100;
    public const int EventKeyMaxLength = 200;
    public const int RelatedItemTypeMaxLength = 50;
    public const int RelatedItemIdMaxLength = 100;
    public const int LanguageMaxLength = 10;
    public const int CorrelationIdMaxLength = 100;
    public const int LeaseOwnerMaxLength = 200;
    public const int FanOutCursorMaxLength = 450;
    public const int LastErrorMaxLength = 1000;
    public const int VariablesMaxBytes = 16 * 1024;

    /// <summary>After this many failed dispatch attempts the event is left <see cref="OutboxEventStatus.Failed"/> for an administrator.</summary>
    public const int MaxDispatchAttempts = 10;

    public string Type { get; private set; } = string.Empty;

    /// <summary>Not unique here: de-duplication happens at materialization, so a duplicate never rolls back the emitter (ADR 0018).</summary>
    public string? EventKey { get; private set; }

    public string RecipientJson { get; private set; } = string.Empty;

    public string VariablesJson { get; private set; } = "{}";

    public string? RelatedItemType { get; private set; }

    public string? RelatedItemId { get; private set; }

    /// <summary>The related item's parent, for nested routes such as an execution under its workflow (R11 addendum).</summary>
    public string? RelatedItemParentId { get; private set; }

    /// <summary>The first language candidate (FR-044).</summary>
    public string? ExplicitLanguage { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public DateTime OccurredAtUtc { get; private set; }

    public OutboxEventStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    /// <summary>The last user id processed by a batched fan-out (R22).</summary>
    public string? FanOutCursor { get; private set; }

    public OutboxEventOutcome? Outcome { get; private set; }

    /// <summary>A safe summary; the full exception goes to the log with the correlation id.</summary>
    public string? LastError { get; private set; }

    public DateTime? ProcessedAtUtc { get; private set; }

    private NotificationOutboxEvent()
    {
        // Required by EF Core materialization.
    }

    public static NotificationOutboxEvent Create(
        string type,
        string recipientJson,
        string variablesJson,
        string correlationId,
        DateTime occurredAtUtc,
        string? eventKey = null,
        string? relatedItemType = null,
        string? relatedItemId = null,
        string? relatedItemParentId = null,
        string? explicitLanguage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(variablesJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        RequireMaxLength(type, TypeMaxLength, "type");
        RequireMaxLength(eventKey, EventKeyMaxLength, "event key");
        RequireMaxLength(relatedItemType, RelatedItemTypeMaxLength, "related item type");
        RequireMaxLength(relatedItemId, RelatedItemIdMaxLength, "related item id");
        RequireMaxLength(relatedItemParentId, RelatedItemIdMaxLength, "related item parent id");
        RequireMaxLength(explicitLanguage, LanguageMaxLength, "language");
        if (Encoding.UTF8.GetByteCount(variablesJson) > VariablesMaxBytes)
        {
            throw new DomainRuleViolationException($"Notification variables can't exceed {VariablesMaxBytes} bytes.");
        }

        return new NotificationOutboxEvent
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            EventKey = eventKey,
            RecipientJson = recipientJson,
            VariablesJson = variablesJson,
            RelatedItemType = relatedItemType,
            RelatedItemId = relatedItemId,
            RelatedItemParentId = relatedItemParentId,
            ExplicitLanguage = explicitLanguage,
            CorrelationId = correlationId.Length > CorrelationIdMaxLength ? correlationId[..CorrelationIdMaxLength] : correlationId,
            OccurredAtUtc = occurredAtUtc,
            Status = OutboxEventStatus.Pending,
            NextAttemptAtUtc = occurredAtUtc,
            CreatedAtUtc = occurredAtUtc,
        };
    }

    /// <summary>The claim (R4). Persistence performs the same move as one conditional update.</summary>
    public void Claim(string workerId, DateTime leaseExpiresAtUtc, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        var leaseExpired = Status == OutboxEventStatus.Processing && LeaseExpiresAtUtc is { } expires && expires <= now;
        if (Status != OutboxEventStatus.Pending && !leaseExpired)
        {
            throw Invalid(nameof(Claim));
        }

        Status = OutboxEventStatus.Processing;
        Attempts++;
        LeaseOwner = workerId;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public void Complete(OutboxEventOutcome outcome, DateTime now)
    {
        RequireProcessing(nameof(Complete));
        Status = OutboxEventStatus.Completed;
        Outcome = outcome;
        ProcessedAtUtc = now;
        NextAttemptAtUtc = null;
        ClearLease();
    }

    /// <summary>A fan-out batch is done and more recipients remain: back to pending at once, cursor advanced (R22).</summary>
    public void AdvanceFanOut(string cursor, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cursor);
        RequireProcessing(nameof(AdvanceFanOut));
        Status = OutboxEventStatus.Pending;
        FanOutCursor = cursor;
        NextAttemptAtUtc = now;

        // A completed batch is progress, not a failed attempt.
        Attempts = Math.Max(0, Attempts - 1);
        ClearLease();
    }

    /// <summary>A dispatch error: back off, or give up after <see cref="MaxDispatchAttempts"/>.</summary>
    public void Release(string safeError, DateTime nextAttemptAtUtc, DateTime now)
    {
        RequireProcessing(nameof(Release));
        LastError = safeError.Length > LastErrorMaxLength ? safeError[..LastErrorMaxLength] : safeError;
        ClearLease();

        if (Attempts >= MaxDispatchAttempts)
        {
            Status = OutboxEventStatus.Failed;
            ProcessedAtUtc = now;
            NextAttemptAtUtc = null;
            return;
        }

        Status = OutboxEventStatus.Pending;
        NextAttemptAtUtc = nextAttemptAtUtc;
    }

    private void RequireProcessing(string operation)
    {
        if (Status != OutboxEventStatus.Processing)
        {
            throw Invalid(operation);
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
    }

    private DomainRuleViolationException Invalid(string operation) =>
        new($"A {Status} outbox event can't be changed by {operation}.");

    private static void RequireMaxLength(string? value, int maxLength, string field)
    {
        if (value is { Length: var length } && length > maxLength)
        {
            throw new DomainRuleViolationException($"The notification event {field} can't exceed {maxLength} characters.");
        }
    }
}
