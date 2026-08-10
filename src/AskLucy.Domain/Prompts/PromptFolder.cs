using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>
/// A node in a user's prompt-folder hierarchy (spec.md FR-050, FR-054, research.md Decision 5).
/// <see cref="Depth"/> is computed at create/move time and stored (not recomputed per-read) so the
/// nesting-depth check is a cheap comparison, not a recursive query — mirrors
/// <c>KnowledgeBaseFolder</c> exactly.
/// </summary>
public sealed class PromptFolder : BaseEntity
{
    public string OwnerId { get; private set; } = string.Empty;

    public Guid? ParentFolderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    private PromptFolder()
    {
        // Required by EF Core materialization.
    }

    public static PromptFolder Create(string ownerId, string name, Guid? parentFolderId, int parentDepth, int maxNestingDepth, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A folder name is required.");
        }

        var depth = parentFolderId is null ? 0 : parentDepth + 1;
        if (depth > maxNestingDepth)
        {
            throw new DomainRuleViolationException($"Folders cannot be nested deeper than {maxNestingDepth} levels.");
        }

        return new PromptFolder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            ParentFolderId = parentFolderId,
            Name = name.Trim(),
            Depth = depth,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Rename(string name, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A folder name is required.");
        }

        Name = name.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void MoveTo(Guid? newParentFolderId, int newParentDepth, int maxNestingDepth, string actor)
    {
        var depth = newParentFolderId is null ? 0 : newParentDepth + 1;
        if (depth > maxNestingDepth)
        {
            throw new DomainRuleViolationException($"Folders cannot be nested deeper than {maxNestingDepth} levels.");
        }

        ParentFolderId = newParentFolderId;
        Depth = depth;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
