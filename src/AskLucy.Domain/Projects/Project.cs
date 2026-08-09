using AskLucy.Domain.Common;

namespace AskLucy.Domain.Projects;

/// <summary>
/// A user-created, named workspace that groups a set of related conversations, used to scope
/// Project Memory (spec.md FR-002a, FR-002b, User Story 5, Key Entity "Project"). Deliberately
/// minimal per FR-002b's explicit scope limit — a name and its member conversations only.
///
/// <para>Soft-deleted (<see cref="BaseEntity.IsDeleted"/>), not hard-deleted, so historical
/// <c>UserChat.ProjectId</c>/<c>Memory.ProjectId</c> values remain resolvable for display.
/// Deletion cascades to archive (never delete) its scoped memories — implemented as a direct
/// repository call within <c>DeleteProjectCommandHandler</c>, not a dispatched domain event
/// (research.md Decision 15 assumed an existing domain-event dispatch mechanism; none exists in
/// this codebase, discovered during <c>/speckit-implement</c> — the simpler, already-established
/// precedent of one Application handler directly calling another bounded context's repository
/// within the same transaction is used instead, mirroring
/// <c>UpdateConversationKnowledgeBasesCommandHandler</c>'s cross-context repository access).</para>
/// </summary>
public sealed class Project : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    private Project()
    {
        // Required by EF Core materialization.
    }

    public static Project Create(string userId, string name, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A project must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainRuleViolationException("A project must have a name.");
        }

        return new Project
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Rename(string newName, string actor)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new DomainRuleViolationException("A project must have a name.");
        }

        Name = newName.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void SoftDelete(string actor)
    {
        DeletedAtUtc = DateTime.UtcNow;
        DeletedBy = actor;
    }

    public bool IsOwnedBy(string userId) => UserId == userId;
}
