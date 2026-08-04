using AskLucy.Domain.Common;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>
/// A node in a knowledge base's folder hierarchy (FR-012-FR-016). <see cref="Depth"/> is
/// computed at create/move time and stored (not recomputed per-read) so the nesting-depth
/// check is a cheap comparison, not a recursive query (data-model.md).
/// </summary>
public sealed class KnowledgeBaseFolder : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public Guid? ParentFolderId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Depth { get; private set; }

    private KnowledgeBaseFolder()
    {
        // Required by EF Core materialization.
    }

    public static KnowledgeBaseFolder Create(Guid knowledgeBaseId, string name, Guid? parentFolderId, int parentDepth, int maxNestingDepth, string actor)
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

        return new KnowledgeBaseFolder
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
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
