using System.Text;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.Notifications;

/// <summary>
/// One message for one recipient about one event (FR-010; data-model.md). Aggregate root of its
/// <see cref="NotificationDelivery"/> rows; <see cref="Status"/> is aggregated from them (R8).
/// </summary>
public sealed class Notification : BaseEntity
{
    public const int TypeMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int MessageMaxLength = 2000;
    public const int LanguageMaxLength = 10;
    public const int RelatedItemTypeMaxLength = 50;
    public const int RelatedItemIdMaxLength = 100;
    public const int ActionRouteMaxLength = 300;
    public const int ActionLabelMaxLength = 60;
    public const int CorrelationIdMaxLength = 100;
    public const int EventKeyMaxLength = 200;
    public const int MetadataMaxBytes = 4 * 1024;

    private readonly List<NotificationDelivery> _deliveries = [];

    /// <summary>Null only for support-mailbox and address-only account emails that have no user.</summary>
    public string? RecipientUserId { get; private set; }

    public NotificationCategory Category { get; private set; }

    public string Type { get; private set; } = string.Empty;

    /// <summary>Rendered in-app title, plain text.</summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>Rendered in-app message, plain text, never HTML.</summary>
    public string Message { get; private set; } = string.Empty;

    public NotificationPriority Priority { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>The language the notification was produced in (FR-044c).</summary>
    public string Language { get; private set; } = "en";

    /// <summary>The in-app template version used (FR-039); null for legacy imports.</summary>
    public Guid? TemplateVersionId { get; private set; }

    public string? RelatedItemType { get; private set; }

    public string? RelatedItemId { get; private set; }

    /// <summary>App-relative route built by the link builder (FR-047).</summary>
    public string? ActionRoute { get; private set; }

    /// <summary>The rendered label for <see cref="ActionRoute"/>, in <see cref="Language"/>.</summary>
    public string? ActionLabel { get; private set; }

    /// <summary>Non-sensitive display metadata only (FR-013).</summary>
    public string? MetadataJson { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    /// <summary>De-duplication identity (FR-008), unique per recipient.</summary>
    public string? EventKey { get; private set; }

    public Guid? SourceEventId { get; private set; }

    /// <summary>False for email-only account types (FR-009c) and for notifications with no in-app copy.</summary>
    public bool ShowInCenter { get; private set; }

    public DateTime? ReadAtUtc { get; private set; }

    public DateTime? ExpiresAtUtc { get; private set; }

    public IReadOnlyCollection<NotificationDelivery> Deliveries => _deliveries.AsReadOnly();

    public bool IsRead => ReadAtUtc is not null;

    private Notification()
    {
        // Required by EF Core materialization.
    }

    public static Notification Create(
        string? recipientUserId,
        NotificationTypeDefinition definition,
        NotificationPriority priority,
        string title,
        string message,
        string language,
        string correlationId,
        DateTime now,
        bool showInCenter,
        Guid? templateVersionId = null,
        string? relatedItemType = null,
        string? relatedItemId = null,
        string? actionRoute = null,
        string? metadataJson = null,
        string? eventKey = null,
        Guid? sourceEventId = null,
        DateTime? expiresAtUtc = null,
        string? actionLabel = null,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        var inCenter = showInCenter && definition.ShowInCenter;
        if (inCenter)
        {
            if (string.IsNullOrWhiteSpace(recipientUserId))
            {
                throw new DomainRuleViolationException("A notification shown in the center needs a recipient user.");
            }

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                throw new DomainRuleViolationException("A notification shown in the center needs a title and a message.");
            }
        }

        RequireMaxLength(title, TitleMaxLength, "title");
        RequireMaxLength(message, MessageMaxLength, "message");
        RequireMaxLength(language, LanguageMaxLength, "language");
        RequireMaxLength(correlationId, CorrelationIdMaxLength, "correlation id");
        RequireMaxLength(relatedItemType, RelatedItemTypeMaxLength, "related item type");
        RequireMaxLength(relatedItemId, RelatedItemIdMaxLength, "related item id");
        RequireMaxLength(actionRoute, ActionRouteMaxLength, "action route");
        RequireMaxLength(actionLabel, ActionLabelMaxLength, "action label");
        RequireMaxLength(eventKey, EventKeyMaxLength, "event key");
        if (metadataJson is not null && Encoding.UTF8.GetByteCount(metadataJson) > MetadataMaxBytes)
        {
            throw new DomainRuleViolationException($"Notification metadata can't exceed {MetadataMaxBytes} bytes.");
        }

        if (actionRoute is not null && !actionRoute.StartsWith('/'))
        {
            throw new DomainRuleViolationException("A notification action route must be app-relative.");
        }

        return new Notification
        {
            // A caller may pre-generate the id when a route must point at the notification itself.
            Id = id ?? Guid.CreateVersion7(),
            RecipientUserId = string.IsNullOrWhiteSpace(recipientUserId) ? null : recipientUserId,
            Category = definition.Category,
            Type = definition.Key,
            Title = title ?? string.Empty,
            Message = message ?? string.Empty,
            Priority = priority,
            Status = NotificationStatus.Created,
            Language = language,
            TemplateVersionId = templateVersionId,
            RelatedItemType = relatedItemType,
            RelatedItemId = relatedItemId,
            ActionRoute = actionRoute,
            ActionLabel = actionRoute is null ? null : actionLabel,
            MetadataJson = metadataJson,
            CorrelationId = correlationId,
            EventKey = eventKey,
            SourceEventId = sourceEventId,
            ShowInCenter = inCenter,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = now,
        };
    }

    /// <summary>Adds one channel's delivery. A notification has at most one delivery per channel.</summary>
    public void AddDelivery(NotificationDelivery delivery)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        if (_deliveries.Any(d => d.Channel == delivery.Channel))
        {
            throw new DomainRuleViolationException($"This notification already has a {delivery.Channel} delivery.");
        }

        delivery.AttachTo(Id);
        _deliveries.Add(delivery);
        RecomputeStatus();
    }

    /// <summary>Idempotent. Only the owner reads, and only once the notification has reached them (FR-011).</summary>
    public void MarkRead(DateTime now)
    {
        if (ReadAtUtc is not null)
        {
            return;
        }

        if (Status is not (NotificationStatus.Delivered or NotificationStatus.Sent))
        {
            throw new DomainRuleViolationException($"A {Status} notification can't be marked read.");
        }

        ReadAtUtc = now;
        Status = NotificationStatus.Read;
    }

    /// <summary>Owner deletion (FR-016a): hides the notification from every user-facing query. Idempotent.</summary>
    public void DeleteByOwner(string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!string.Equals(RecipientUserId, userId, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Only the recipient can delete a notification.");
        }

        if (DeletedAtUtc is not null)
        {
            return;
        }

        DeletedAtUtc = now;
        DeletedBy = userId;
    }

    /// <summary>Expires every delivery still waiting to be sent (announcement end, request validity).</summary>
    public void Expire(DateTime now)
    {
        ExpiresAtUtc ??= now;
        foreach (var delivery in _deliveries.Where(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Retrying))
        {
            delivery.Expire();
        }

        RecomputeStatus();
    }

    /// <summary>Cancels every delivery still waiting to be sent, e.g. the recipient was deleted.</summary>
    public void Cancel(string safeReason)
    {
        foreach (var delivery in _deliveries.Where(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Retrying))
        {
            delivery.Cancel(safeReason);
        }

        RecomputeStatus();
    }

    /// <summary>An administrator retry of one failed delivery; refused for a deleted or expired notification (spec edge case).</summary>
    public void RetryDelivery(Guid deliveryId, DateTime now)
    {
        if (DeletedAtUtc is not null)
        {
            throw new DomainRuleViolationException("A deleted notification can't be retried.");
        }

        if (ExpiresAtUtc is { } expires && expires <= now)
        {
            throw new DomainRuleViolationException("An expired notification can't be retried.");
        }

        var delivery = _deliveries.SingleOrDefault(d => d.Id == deliveryId)
            ?? throw new DomainRuleViolationException("The delivery doesn't belong to this notification.");
        delivery.ResetForRetry(now);
        RecomputeStatus();
    }

    /// <summary>
    /// Re-aggregates <see cref="Status"/> from the deliveries (R8, data-model.md § State machines).
    /// <see cref="NotificationStatus.Read"/> never regresses.
    /// </summary>
    public void RecomputeStatus()
    {
        if (ReadAtUtc is not null)
        {
            Status = NotificationStatus.Read;
            return;
        }

        var active = _deliveries.Where(d => d.Status != DeliveryStatus.Skipped).ToList();
        if (_deliveries.Count == 0)
        {
            Status = NotificationStatus.Created;
            return;
        }

        if (active.Count == 0)
        {
            // Every channel was skipped: nothing will reach the recipient.
            Status = NotificationStatus.Cancelled;
            return;
        }

        Status = active switch
        {
            _ when active.Any(d => d.Status == DeliveryStatus.Delivered) => NotificationStatus.Delivered,
            _ when active.Any(d => d.Status == DeliveryStatus.Sent) => NotificationStatus.Sent,
            _ when active.Any(d => d.Status == DeliveryStatus.Sending) => NotificationStatus.Processing,
            _ when active.Any(d => d.Status is DeliveryStatus.Pending or DeliveryStatus.Retrying) => NotificationStatus.Queued,
            _ when active.Any(d => d.Status is DeliveryStatus.Failed or DeliveryStatus.DeadLettered) => NotificationStatus.Failed,
            _ when active.Any(d => d.Status == DeliveryStatus.Expired) => NotificationStatus.Expired,
            _ => NotificationStatus.Cancelled,
        };
    }

    private static void RequireMaxLength(string? value, int maxLength, string field)
    {
        if (value is { Length: var length } && length > maxLength)
        {
            throw new DomainRuleViolationException($"The notification {field} can't exceed {maxLength} characters.");
        }
    }
}
