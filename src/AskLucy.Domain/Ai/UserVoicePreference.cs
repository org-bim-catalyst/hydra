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
}
