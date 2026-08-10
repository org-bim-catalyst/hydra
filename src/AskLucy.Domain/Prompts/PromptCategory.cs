using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>
/// A classification value, predefined-and-shared or custom-and-private (spec.md FR-050,
/// research.md Decision 6). <see cref="OwnerId"/> is the sole predefined/custom discriminator: null
/// means predefined and shared platform-wide (a small seeded set); non-null means custom and
/// private to that owner — mirrors <c>KnowledgeBaseCategory</c> exactly.
/// </summary>
public sealed class PromptCategory : BaseEntity
{
    public string? OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsPredefined => OwnerId is null;

    private PromptCategory()
    {
        // Required by EF Core materialization.
    }

    public static PromptCategory CreatePredefined(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A category name is required.");
        }

        return new PromptCategory
        {
            Id = Guid.CreateVersion7(),
            OwnerId = null,
            Name = name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = "system",
        };
    }

    public static PromptCategory CreateCustom(string name, string ownerId, string actor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A category name is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A custom category must belong to a user.");
        }

        return new PromptCategory
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
