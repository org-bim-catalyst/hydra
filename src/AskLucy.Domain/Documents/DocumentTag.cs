using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>
/// A user-defined label attachable to one or more <see cref="Document"/>s (FR-032, data-model.md)
/// — private to the creating user, unlike <c>KnowledgeBaseTag</c> (which is scoped one-to-one
/// per knowledge base): the same <see cref="DocumentTag"/> row is genuinely shared/reused across
/// all of one user's documents, so the Document&lt;-&gt;DocumentTag relationship is many-to-many
/// (an implicit EF Core join table, configured in Persistence — not a mapped Domain type, per
/// data-model.md).
/// </summary>
public sealed class DocumentTag : BaseEntity
{
    public string OwnerId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    private DocumentTag()
    {
        // Required by EF Core materialization.
    }

    public static DocumentTag Create(string ownerId, string name, string actor)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new DomainRuleViolationException("A tag must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A tag name is required.");
        }

        return new DocumentTag
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Name = name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
