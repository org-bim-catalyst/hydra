using AskLucy.Domain.Common;

namespace AskLucy.Domain.Panels;

/// <summary>
/// A user's floating-panel opacity preference (spec 028 FR-011/FR-012) — created lazily on first
/// save, not at registration, same convention as <see cref="AskLucy.Domain.Ai.UserVoicePreference"/>.
/// A separate aggregate from <c>UserVoicePreference</c> (research.md Decision 6, SRP) — panel
/// transparency and voice persona are unrelated concerns that would only share a table by
/// coincidence of both being "a small user preference."
/// </summary>
public sealed class UserPanelPreference : BaseEntity
{
    public const int MinOpacityPercent = 40;
    public const int MaxOpacityPercent = 100;
    public const int DefaultOpacityPercent = 85;

    public string UserId { get; private set; } = string.Empty;

    /// <summary>Clarifications Q4 — bounded <c>[40, 100]</c> so a panel can never be configured to
    /// practical invisibility.</summary>
    public int OpacityPercent { get; private set; } = DefaultOpacityPercent;

    private UserPanelPreference()
    {
        // Required by EF Core materialization.
    }

    public static UserPanelPreference Create(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A panel preference must belong to a user.");
        }

        return new UserPanelPreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>Clamped as defense-in-depth — the user-facing rejection for an out-of-range value
    /// is <c>SaveUserPanelPreferenceCommandValidator</c> (FluentValidation), not this clamp.</summary>
    public void SetOpacityPercent(int opacityPercent, string actor)
    {
        OpacityPercent = Math.Clamp(opacityPercent, MinOpacityPercent, MaxOpacityPercent);
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
