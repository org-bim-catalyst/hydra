using AskLucy.Domain.Common;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>
/// A classification value, predefined-and-shared or custom-and-private (FR-017-FR-019,
/// FR-038). <see cref="OwnerId"/> is the sole predefined/custom discriminator: null means
/// predefined and shared platform-wide (the 8 seeded categories); non-null means custom and
/// private to that owner (data-model.md).
/// </summary>
public sealed class KnowledgeBaseCategory : BaseEntity
{
    public string? OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsPredefined => OwnerId is null;

    private KnowledgeBaseCategory()
    {
        // Required by EF Core materialization.
    }

    public static KnowledgeBaseCategory CreateCustom(string name, string ownerId, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A category name is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A custom category must belong to a user.");
        }

        return new KnowledgeBaseCategory
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
