using AskLucy.Domain.Ai;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai;

/// <summary>specs/070 — the voice provider row that orders Lucy's text-to-speech engines.</summary>
public sealed class VoiceProviderTests
{
    [Fact]
    public void Create_ShouldTrimAndStartWithoutCredentialOrVoice()
    {
        var provider = VoiceProvider.Create(" Supertonic ", " Supertonic (on-server) ", 1, "admin-1");

        provider.ProviderKey.Should().Be("Supertonic");
        provider.DisplayName.Should().Be("Supertonic (on-server)");
        provider.Priority.Should().Be(1);
        provider.DefaultVoiceId.Should().BeNull();
        provider.CredentialCiphertext.Should().BeNull();
        provider.CreatedBy.Should().Be("admin-1");
    }

    [Theory]
    [InlineData("", "Name", 0)]
    [InlineData("  ", "Name", 0)]
    [InlineData("Key", "", 0)]
    [InlineData("Key", "Name", -1)]
    public void Create_ShouldReject_BlankKeyOrNameOrNegativePriority(string key, string name, int priority)
    {
        var act = () => VoiceProvider.Create(key, name, priority, "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SetCredential_ShouldStoreCiphertextAndHint_AndStampTheRotation()
    {
        var provider = VoiceProvider.Create("ElevenLabs", "ElevenLabs", 0, "system");

        provider.SetCredential("ciphertext", "sk_1...9XYZ", "admin-1");

        provider.CredentialCiphertext.Should().Be("ciphertext");
        provider.CredentialHint.Should().Be("sk_1...9XYZ");
        provider.CredentialLastRotatedAtUtc.Should().NotBeNull();
        provider.ModifiedBy.Should().Be("admin-1");
    }

    [Fact]
    public void SetDefaultVoice_ShouldTrim()
    {
        var provider = VoiceProvider.Create("Supertonic", "Supertonic", 0, "system");

        provider.SetDefaultVoice(" F2 ", "admin-1");

        provider.DefaultVoiceId.Should().Be("F2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDefaultVoice_ShouldReject_ABlankVoice(string voiceId)
    {
        var provider = VoiceProvider.Create("Supertonic", "Supertonic", 0, "system");

        var act = () => provider.SetDefaultVoice(voiceId, "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SetDefaultVoice_ShouldReject_AnOverlongVoiceId()
    {
        var provider = VoiceProvider.Create("Supertonic", "Supertonic", 0, "system");

        var act = () => provider.SetDefaultVoice(new string('v', VoiceProvider.MaxVoiceIdLength + 1), "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void SetPriority_ShouldNotTouchTheRow_WhenThePriorityIsUnchanged()
    {
        var provider = VoiceProvider.Create("Supertonic", "Supertonic", 2, "system");

        provider.SetPriority(2, "admin-1");

        provider.ModifiedAtUtc.Should().BeNull();
    }

    [Fact]
    public void SetPriority_ShouldReject_ANegativePriority()
    {
        var provider = VoiceProvider.Create("Supertonic", "Supertonic", 2, "system");

        var act = () => provider.SetPriority(-1, "admin-1");

        act.Should().Throw<DomainRuleViolationException>();
    }
}
