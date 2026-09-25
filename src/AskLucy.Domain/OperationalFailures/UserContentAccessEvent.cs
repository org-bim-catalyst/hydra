using AskLucy.Domain.Common;

namespace AskLucy.Domain.OperationalFailures;

/// <summary>
/// Immutable record that a staff member viewed another user's content from an incident
/// (specs/074 FR-016c). Written synchronously before the content is loaded and never purged by
/// retention. The owner's erasure only clears the owner and item references.
/// </summary>
public sealed class UserContentAccessEvent : BaseEntity
{
    public DateTime OccurredAtUtc { get; private set; }

    public string ViewerUserId { get; private set; } = string.Empty;

    public string? OwnerUserId { get; private set; }

    public InvestigatedItemType ItemType { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid IncidentId { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public bool IsOwnerErased { get; private set; }

    private UserContentAccessEvent()
    {
        // Required by EF Core materialization.
    }

    public static UserContentAccessEvent Record(
        string viewerUserId,
        string ownerUserId,
        InvestigatedItemType itemType,
        Guid itemId,
        Guid incidentId,
        string correlationId,
        DateTime occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new UserContentAccessEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAtUtc = occurredAtUtc,
            ViewerUserId = viewerUserId,
            OwnerUserId = ownerUserId,
            ItemType = itemType,
            ItemId = itemId,
            IncidentId = incidentId,
            CorrelationId = correlationId,
        };
    }
}
