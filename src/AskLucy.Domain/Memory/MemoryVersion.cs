using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>Why a memory's content changed (spec.md FR-015, FR-019).</summary>
public enum MemoryChangeReason
{
    UserEdit,
    ConflictResolutionSupersede,
    SystemReinforcement,
}

/// <summary>
/// An immutable snapshot of a memory's prior content, captured on every content change (spec.md
/// FR-009, FR-019, Key Entity "Memory Version / History Entry"). Append-only — no mutation
/// methods, created only via <see cref="Create"/>.
/// </summary>
public sealed class MemoryVersion : BaseEntity
{
    public Guid MemoryId { get; private set; }

    /// <summary>Encrypted at rest — same converter as <see cref="Memory.Content"/> (research.md Decision 12).</summary>
    public string PreviousContent { get; private set; } = string.Empty;

    public MemoryChangeReason ChangeReason { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public string ChangedByActor { get; private set; } = string.Empty;

    private MemoryVersion()
    {
        // Required by EF Core materialization.
    }

    public static MemoryVersion Create(Guid memoryId, string previousContent, MemoryChangeReason changeReason, string actor)
    {
        var now = DateTime.UtcNow;

        return new MemoryVersion
        {
            Id = Guid.CreateVersion7(),
            MemoryId = memoryId,
            PreviousContent = previousContent,
            ChangeReason = changeReason,
            ChangedAtUtc = now,
            ChangedByActor = actor,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }
}
