using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

public sealed class UserVoicePreferenceTests
{
    [Fact]
    public void Create_ShouldDefaultToPushToTalkAndUnmuted()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");

        preference.UserId.Should().Be("user-1");
        preference.ConversationMode.Should().Be(VoiceConversationMode.PushToTalk);
        preference.IsMuted.Should().BeFalse();
        preference.SelectedVoiceId.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrow_WhenUserIdIsBlank(string blankUserId)
    {
        var act = () => UserVoicePreference.Create(blankUserId, "user-1");
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SetConversationMode_ShouldUpdateModeAndAudit()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");

        preference.SetConversationMode(VoiceConversationMode.Continuous, "user-1");

        preference.ConversationMode.Should().Be(VoiceConversationMode.Continuous);
        preference.ModifiedAtUtc.Should().NotBeNull();
        preference.ModifiedBy.Should().Be("user-1");
    }

    [Fact]
    public void SetPreferences_ShouldUpdateAllFields()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");

        preference.SetPreferences(
            isMuted: true,
            selectedVoiceId: "voice-123",
            voiceSpeed: 1.2,
            voiceStyle: 0.4,
            preferredMicrophoneDeviceId: "mic-1",
            preferredSpeakerDeviceId: "speaker-1",
            actor: "user-1");

        preference.IsMuted.Should().BeTrue();
        preference.SelectedVoiceId.Should().Be("voice-123");
        preference.VoiceSpeed.Should().Be(1.2);
        preference.VoiceStyle.Should().Be(0.4);
        preference.PreferredMicrophoneDeviceId.Should().Be("mic-1");
        preference.PreferredSpeakerDeviceId.Should().Be("speaker-1");
    }

    [Fact]
    public void SetDefaultLanguage_ShouldUpdateLanguageAndAudit()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");

        preference.SetDefaultLanguage("fr", "user-1");

        preference.DefaultLanguage.Should().Be("fr");
        preference.ModifiedAtUtc.Should().NotBeNull();
        preference.ModifiedBy.Should().Be("user-1");
    }

    [Fact]
    public void SetDefaultLanguage_ShouldAllowClearingBackToNull()
    {
        var preference = UserVoicePreference.Create("user-1", "user-1");
        preference.SetDefaultLanguage("fr", "user-1");

        preference.SetDefaultLanguage(null, "user-1");

        preference.DefaultLanguage.Should().BeNull();
    }
}
