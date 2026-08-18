using AskLucy.Domain.Common;

namespace AskLucy.Domain.Prompts;

/// <summary>
/// A free-form label assignable to a <see cref="Prompt"/> (spec.md FR-050, FR-052, research.md
/// Decision 6) — a per-prompt value row, not a reference into a deduplicated master tag catalog,
/// mirroring <c>KnowledgeBaseTag</c>. Child of <see cref="Prompt"/>'s aggregate, created only via
/// <see cref="Prompt.AddTag"/>.
/// </summary>
public sealed class PromptTag : BaseEntity
{
    public Guid PromptId { get; private set; }

    public string OwnerId { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    private PromptTag()
    {
        // Required by EF Core materialization.
    }

    internal static PromptTag Create(Guid promptId, string ownerId, string value, string actor)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException("A tag value is required.");
        }

        return new PromptTag
        {
            Id = Guid.CreateVersion7(),
            PromptId = promptId,
            OwnerId = ownerId,
            Value = value.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    internal void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }
}
