using AskLucy.Domain.Common;

namespace AskLucy.Domain.KnowledgeBases;

/// <summary>
/// A free-form label assignable to a knowledge base (FR-017-FR-021). Per-knowledge-base value
/// row, not a reference into a deduplicated master "tag catalog" table — a tag carries no
/// attributes beyond its text, so a master table would add a join with no behavior it enables
/// that this shape doesn't already provide (data-model.md "Explicitly Not Modeled"). Child of
/// <see cref="KnowledgeBase"/>'s aggregate, created only via <see cref="KnowledgeBase.AddTag"/>.
/// </summary>
public sealed class KnowledgeBaseTag : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public string OwnerId { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    private KnowledgeBaseTag()
    {
        // Required by EF Core materialization.
    }

    internal static KnowledgeBaseTag Create(Guid knowledgeBaseId, string ownerId, string value, string actor)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleViolationException("A tag value is required.");
        }

        return new KnowledgeBaseTag
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            OwnerId = ownerId,
            Value = value.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
