using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A user's saved chat entry. Migrated from the legacy int-keyed <c>UserChats</c> table
/// onto the standard entity conventions (spec.md FR-024); title/session/timestamp fields
/// are otherwise unchanged. Rename/delete (FR-033) are the only new capabilities added
/// during this migration, per spec.md &#167; Clarifications.
/// </summary>
public sealed class UserChat : BaseEntity
{
    public string Title { get; private set; } = string.Empty;

    public string? SessionId { get; private set; }

    public string UserId { get; private set; } = string.Empty;

    private UserChat()
    {
        // Required by EF Core materialization.
    }

    public static UserChat Create(string title, string userId, string? sessionId, string actor)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainRuleViolationException("A chat title is required.");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A chat must belong to a user.");
        }

        return new UserChat
        {
            Id = Guid.CreateVersion7(),
            Title = title.Trim(),
            SessionId = sessionId,
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = userId,
        };
    }

    public void Rename(string newTitle, string actor)
    {
        if (string.IsNullOrWhiteSpace(newTitle))
        {
            throw new DomainRuleViolationException("A chat title is required.");
        }

        Title = newTitle.Trim();
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
