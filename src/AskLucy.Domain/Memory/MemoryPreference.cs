using AskLucy.Domain.Common;

namespace AskLucy.Domain.Memory;

/// <summary>
/// Account-level memory settings (spec.md FR-022, FR-025, Key Entity "Memory Preference"). One
/// row per user, enforced via a unique index on <see cref="UserId"/> — uses the standard surrogate
/// <see cref="BaseEntity.Id"/> key rather than <see cref="UserId"/> itself as the primary key
/// (data-model.md originally proposed the latter; corrected during <c>/speckit-implement</c>
/// since a natural key as PK violates constitution §5).
/// </summary>
public sealed class MemoryPreference : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    /// <summary>spec.md FR-022 — the account-level on/off switch. Defaults <c>true</c> (memory is opt-out), consistent with FR-007's "new accounts default to automatic mode."</summary>
    public bool MemoryEnabled { get; private set; } = true;

    private MemoryPreference()
    {
        // Required by EF Core materialization.
    }

    public static MemoryPreference CreateDefault(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("Memory preferences must belong to a user.");
        }

        return new MemoryPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            MemoryEnabled = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void SetMemoryEnabled(bool enabled, string actor)
    {
        MemoryEnabled = enabled;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
