using AskLucy.Domain.Common;

namespace AskLucy.Domain.Chats;

/// <summary>
/// A user's conversation-behaviour preferences (specs/045-conversational-agent-runtime FR-032) —
/// created lazily on first save, not at registration, the same convention
/// <see cref="AskLucy.Domain.Panels.UserPanelPreference"/> and
/// <see cref="AskLucy.Domain.Ai.UserVoicePreference"/> already follow. An absent row means
/// defaults, so there is no backfill and no migration data step.
/// <para>
/// A separate aggregate rather than another column on one of those: panel opacity, voice persona
/// and whether Lucy suggests next steps are unrelated concerns that would share a table only by
/// the coincidence of all being "a small user preference" (constitution §2.II).
/// </para>
/// </summary>
public sealed class UserConversationPreference : BaseEntity
{
    public const bool DefaultSuggestedActionsEnabled = true;

    public string UserId { get; private set; } = string.Empty;

    /// <summary>
    /// FR-032 — when false, the offer step is skipped entirely: no model call, no event, no card.
    /// Turns still narrate their beats and still perform requested work; offers already in history
    /// render as plain text rather than as interactive rows.
    /// </summary>
    public bool SuggestedActionsEnabled { get; private set; } = DefaultSuggestedActionsEnabled;

    private UserConversationPreference()
    {
        // Required by EF Core materialization.
    }

    public static UserConversationPreference Create(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A conversation preference must belong to a user.");
        }

        return new UserConversationPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void SetSuggestedActionsEnabled(bool enabled, string actor)
    {
        SuggestedActionsEnabled = enabled;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
