using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>A user-organized hierarchical container for <see cref="Document"/>s (FR-033, data-model.md). Single-owner (spec.md Assumptions) — no cross-user/shared folders in this spec.</summary>
public sealed class DocumentFolder : BaseEntity
{
    public string OwnerId { get; private set; } = string.Empty;

    public Guid? ParentFolderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Computed at create/move time and stored (not recomputed per-read), mirroring <c>KnowledgeBaseFolder.Depth</c> (specs/014).</summary>
    public int Depth { get; private set; }

    private DocumentFolder()
    {
        // Required by EF Core materialization.
    }

    public static DocumentFolder Create(string ownerId, string name, Guid? parentFolderId, int depth, string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A folder must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A folder name is required.");
        }

        return new DocumentFolder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            ParentFolderId = parentFolderId,
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

    /// <summary>Moves this folder under a new parent, recomputing its depth (the caller is responsible for the circular-move/cross-owner checks, which need repository-level tree traversal).</summary>
    public void MoveTo(Guid? parentFolderId, int depth, string actor)
    {
        ParentFolderId = parentFolderId;
        Depth = depth;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
