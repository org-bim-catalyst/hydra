using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

public enum VoiceConversationMode
{
    PushToTalk,
    Continuous,
}

/// <summary>
/// A user's personal voice settings (FR-029/FR-030) — created lazily on first save, not at
/// registration, same convention as <see cref="UserAiPreference"/>.
/// </summary>
public sealed class UserVoicePreference : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    public VoiceConversationMode ConversationMode { get; private set; } = VoiceConversationMode.PushToTalk;

    public bool IsMuted { get; private set; }

    public string? SelectedVoiceId { get; private set; }

    public double? VoiceSpeed { get; private set; }

    public double? VoiceStyle { get; private set; }

    public string? PreferredMicrophoneDeviceId { get; private set; }

    public string? PreferredSpeakerDeviceId { get; private set; }

    /// <summary>specs/026-floating-chat-assistant FR-016/FR-017 — the user's default
    /// response language (e.g. "en"), driving the chat widget's read-only flag indicator.
    /// <c>null</c> means no explicit preference has been saved yet (data-model.md).</summary>
    public string? DefaultLanguage { get; private set; }

    private UserVoicePreference()
    {
        // Required by EF Core materialization.
    }

    public static UserVoicePreference Create(string userId, string actor)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A voice preference must belong to a user.");
        }

        return new UserVoicePreference
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>FR-016. Changing mode never restarts an in-progress conversation — that's a
    /// frontend orchestration concern (data-model.md); this only persists the selection.</summary>
    public void SetConversationMode(VoiceConversationMode mode, string actor)
    {
        ConversationMode = mode;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-029/FR-030. Cross-field range validation for speed/style is an
    /// Application-layer concern (data-model.md), not a Domain invariant.</summary>
    public void SetPreferences(
        bool isMuted,
        string? selectedVoiceId,
        double? voiceSpeed,
        double? voiceStyle,
        string? preferredMicrophoneDeviceId,
        string? preferredSpeakerDeviceId,
        string actor)
    {
        IsMuted = isMuted;
        SelectedVoiceId = selectedVoiceId;
        VoiceSpeed = voiceSpeed;
        VoiceStyle = voiceStyle;
        PreferredMicrophoneDeviceId = preferredMicrophoneDeviceId;
        PreferredSpeakerDeviceId = preferredSpeakerDeviceId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>specs/026-floating-chat-assistant FR-017. Allow-list validation against the
    /// product's supported language codes is an Application-layer concern
    /// (data-model.md), not a Domain invariant — mirrors <see cref="SetPreferences"/>'s
    /// own division of responsibility for VoiceSpeed/VoiceStyle.</summary>
    public void SetDefaultLanguage(string? defaultLanguage, string actor)
    {
        DefaultLanguage = defaultLanguage;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
