using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

public enum MemoryConflictType
{
    DirectContradiction,
    AmbiguousSupersedeOrSupplement,
}

public enum MemoryConflictResolutionStatus
{
    AutoResolved,
    PendingUserConfirmation,
    ResolvedKeepExisting,
    ResolvedKeepNew,
    ResolvedKeepBoth,
}

/// <summary>
/// Tracks a detected contradiction/ambiguity between a new candidate and an existing active
/// memory (spec.md FR-015, FR-016 — amended by the 2026-08-09 clarification for asynchronous
/// resolution; research.md Decision 10). A memory with an open (<see cref="MemoryConflictResolutionStatus.PendingUserConfirmation"/>)
/// conflict is excluded from memory-selection ranking until resolved — enforced by the retrieval
/// query, not by mutating <see cref="Memory.State"/> (lifecycle state and conflict status are
/// independently queryable concerns).
/// </summary>
public sealed class MemoryConflict : BaseEntity
{
    public Guid ExistingMemoryId { get; private set; }

    /// <summary>Set when the new statement was itself persisted as a separate candidate pending resolution; null when auto-merged directly into <see cref="ExistingMemoryId"/> as a <see cref="MemoryConflictType.DirectContradiction"/> (see <see cref="MemoryChangeReason.ConflictResolutionSupersede"/> instead).</summary>
    public Guid? NewMemoryId { get; private set; }

    public MemoryConflictType ConflictType { get; private set; }

    public MemoryConflictResolutionStatus ResolutionStatus { get; private set; }

    public DateTime DetectedAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public string? ResolvedByActor { get; private set; }

    private MemoryConflict()
    {
        // Required by EF Core materialization.
    }

    public static MemoryConflict CreateAutoResolved(Guid existingMemoryId, string actor)
    {
        var now = DateTime.UtcNow;

        return new MemoryConflict
        {
            Id = Guid.CreateVersion7(),
            ExistingMemoryId = existingMemoryId,
            ConflictType = MemoryConflictType.DirectContradiction,
            ResolutionStatus = MemoryConflictResolutionStatus.AutoResolved,
            DetectedAtUtc = now,
            ResolvedAtUtc = now,
            ResolvedByActor = actor,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    public static MemoryConflict CreatePendingConfirmation(Guid existingMemoryId, Guid newMemoryId, string actor)
    {
        var now = DateTime.UtcNow;

        return new MemoryConflict
        {
            Id = Guid.CreateVersion7(),
            ExistingMemoryId = existingMemoryId,
            NewMemoryId = newMemoryId,
            ConflictType = MemoryConflictType.AmbiguousSupersedeOrSupplement,
            ResolutionStatus = MemoryConflictResolutionStatus.PendingUserConfirmation,
            DetectedAtUtc = now,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    /// <summary>spec.md FR-016, User Story 6 AC2/AC3.</summary>
    public void Resolve(MemoryConflictResolutionStatus resolution, string actor)
    {
        if (ResolutionStatus != MemoryConflictResolutionStatus.PendingUserConfirmation)
        {
            throw new DomainRuleViolationException("Only a conflict pending user confirmation can be resolved.");
        }

        if (resolution is not (MemoryConflictResolutionStatus.ResolvedKeepExisting
            or MemoryConflictResolutionStatus.ResolvedKeepNew
            or MemoryConflictResolutionStatus.ResolvedKeepBoth))
        {
            throw new DomainRuleViolationException("Invalid resolution outcome.");
        }

        ResolutionStatus = resolution;
        ResolvedAtUtc = DateTime.UtcNow;
        ResolvedByActor = actor;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
