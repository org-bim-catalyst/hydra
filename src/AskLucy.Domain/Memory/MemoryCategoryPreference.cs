using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>spec.md FR-007.</summary>
public enum MemoryApprovalMode
{
    Automatic,
    Manual,
    Disabled,
}

/// <summary>
/// Per-category approval mode and enablement (spec.md FR-007, FR-025). Unique on
/// (<see cref="UserId"/>, <see cref="Category"/>) — a row is created lazily on first access with
/// the defaults below rather than bulk-inserted for every category at account creation.
/// </summary>
public sealed class MemoryCategoryPreference : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public MemoryCategory Category { get; private set; }

    public MemoryApprovalMode ApprovalMode { get; private set; } = MemoryApprovalMode.Automatic;

    /// <summary>spec.md FR-025 — independent of <see cref="ApprovalMode"/>: a category can be <see cref="MemoryApprovalMode.Disabled"/> at the approval-mode level (stop creating new candidates) distinct from <see cref="IsEnabled"/> = false (stop using existing memories in this category entirely too).</summary>
    public bool IsEnabled { get; private set; } = true;

    private MemoryCategoryPreference()
    {
        // Required by EF Core materialization.
    }

    public static MemoryCategoryPreference CreateDefault(string userId, MemoryCategory category, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A category preference must belong to a user.");
        }

        return new MemoryCategoryPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Category = category,
            ApprovalMode = MemoryApprovalMode.Automatic,
            IsEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Update(MemoryApprovalMode? approvalMode, bool? isEnabled, string actor)
    {
        if (approvalMode is not null)
        {
            ApprovalMode = approvalMode.Value;
        }

        if (isEnabled is not null)
        {
            IsEnabled = isEnabled.Value;
        }

        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
