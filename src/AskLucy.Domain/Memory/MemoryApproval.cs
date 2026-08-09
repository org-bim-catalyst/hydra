using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

public enum MemoryApprovalDecision
{
    Pending,
    Approved,
    Rejected,
}

/// <summary>
/// The pending/approved/rejected decision for a candidate memory (spec.md FR-005, FR-007, FR-021,
/// Key Entity "Memory Approval"). At most one row per <see cref="Memory"/> at a time — created
/// when a candidate is detected, resolved (never re-created) when approved/rejected.
/// </summary>
public sealed class MemoryApproval : BaseEntity
{
    public Guid MemoryId { get; private set; }

    public MemoryApprovalDecision Decision { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>User id for a manual decision; a system-actor identifier when <see cref="MemoryApprovalMode.Automatic"/> auto-approves — this is how FR-007's "source disclosed" requirement is satisfied at the data level.</summary>
    public string? DecidedByActor { get; private set; }

    private MemoryApproval()
    {
        // Required by EF Core materialization.
    }

    public static MemoryApproval CreatePending(Guid memoryId, string actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            MemoryId = memoryId,
            Decision = MemoryApprovalDecision.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };

    /// <summary>Records an immediate decision — used for the <see cref="MemoryApprovalMode.Automatic"/> auto-approve path, where no separate pending row is ever observably in a <see cref="MemoryApprovalDecision.Pending"/> state.</summary>
    public static MemoryApproval CreateDecided(Guid memoryId, MemoryApprovalDecision decision, string actor)
    {
        var now = DateTime.UtcNow;

        return new MemoryApproval
        {
            Id = Guid.CreateVersion7(),
            MemoryId = memoryId,
            Decision = decision,
            DecidedAtUtc = now,
            DecidedByActor = actor,
            CreatedAtUtc = now,
            CreatedBy = actor,
        };
    }

    public void Approve(string actor)
    {
        if (Decision != MemoryApprovalDecision.Pending)
        {
            throw new DomainRuleViolationException("Only a pending approval can be approved.");
        }

        Decision = MemoryApprovalDecision.Approved;
        DecidedAtUtc = DateTime.UtcNow;
        DecidedByActor = actor;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Reject(string actor)
    {
        if (Decision != MemoryApprovalDecision.Pending)
        {
            throw new DomainRuleViolationException("Only a pending approval can be rejected.");
        }

        Decision = MemoryApprovalDecision.Rejected;
        DecidedAtUtc = DateTime.UtcNow;
        DecidedByActor = actor;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
