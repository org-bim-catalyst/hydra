using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>spec.md FR-006a; the conflict case from the 2026-08-09 clarification (Q2).</summary>
public enum MemoryNotificationEventType
{
    AutoCreated,
    AutoApproved,
    ConflictNeedsConfirmation,
}

/// <summary>
/// Backing row for the FR-006a / conflict-confirmation low-noise signal (research.md Decision 11),
/// mirroring the existing <c>DocumentNotification</c> persist-then-push idiom.
/// </summary>
public sealed class MemoryNotification : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Loosely references a memory — nullable, no cascade, so a notification remains readable even if its memory is later deleted.</summary>
    public Guid? MemoryId { get; private set; }

    public MemoryNotificationEventType EventType { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public DateTime? ReadAtUtc { get; private set; }

    private MemoryNotification()
    {
        // Required by EF Core materialization.
    }

    public static MemoryNotification Create(string userId, Guid? memoryId, MemoryNotificationEventType eventType, string message, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MemoryId = memoryId,
            EventType = eventType,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };

    public void MarkRead(string actor)
    {
        if (ReadAtUtc is not null)
        {
            return;
        }

        ReadAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
