using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Ai.Dictation;

/// <summary>specs/078 — the platform-wide dictation engine choice and its suspend/revert rules.</summary>
public sealed class DictationEngineSettingTests
{
    private static readonly DateTime Now = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDefault_ShouldUseLocalWhisperForBothEngines_WithNoModel()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);

        setting.Id.Should().Be(DictationEngineSetting.SingletonId);
        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        setting.PushToTalkEngine.Should().Be(DictationClipEngine.LocalWhisper);
        setting.LocalWhisperModelId.Should().BeNull();
        setting.State.Should().Be(DictationEngineState.Active);
        setting.CreatedBy.Should().Be(DictationEngineSetting.SystemActor);
    }

    [Fact]
    public void SetPrimary_ShouldClearASuspension_EvenForTheSameEngine()
    {
        var setting = Suspended(DictationPrimaryEngine.OpenAiWhisper);

        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin-1", Now.AddHours(1));

        setting.State.Should().Be(DictationEngineState.Active);
        setting.SuspendedEngine.Should().BeNull();
        setting.SuspensionReason.Should().BeNull();
        setting.SuspendedAtUtc.Should().BeNull();
        setting.ModifiedBy.Should().Be("admin-1");
    }

    [Fact]
    public void SetPushToTalkEngine_ShouldClearASuspensionOfThatEnginesVendor()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin-1", Now);
        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now).Should().BeTrue();

        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin-1", Now.AddHours(1));

        setting.State.Should().Be(DictationEngineState.Active);
    }

    [Fact]
    public void SetPushToTalkEngine_ShouldKeepASuspensionOfAnotherVendor()
    {
        var setting = Suspended(DictationPrimaryEngine.ElevenLabsRealtime);

        setting.SetPushToTalkEngine(DictationClipEngine.Browser, "admin-1", Now);

        setting.State.Should().Be(DictationEngineState.Suspended);
        setting.SuspendedEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
    }

    [Fact]
    public void SelectLocalWhisperModel_ShouldAcceptAModelAndNull()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        var modelId = Guid.NewGuid();

        setting.SelectLocalWhisperModel(modelId, "admin-1", Now);
        setting.IsSelected(modelId).Should().BeTrue();

        setting.SelectLocalWhisperModel(null, "admin-1", Now);
        setting.LocalWhisperModelId.Should().BeNull();
        setting.IsSelected(modelId).Should().BeFalse();
    }

    [Theory]
    [InlineData(DictationPrimaryEngine.LocalWhisper, DictationCaptureMode.Continuous, DictationTurnEngine.LocalWhisper)]
    [InlineData(DictationPrimaryEngine.LocalWhisper, DictationCaptureMode.PushToTalk, DictationTurnEngine.LocalWhisper)]
    [InlineData(DictationPrimaryEngine.OpenAiWhisper, DictationCaptureMode.Continuous, DictationTurnEngine.OpenAiWhisper)]
    [InlineData(DictationPrimaryEngine.OpenAiWhisper, DictationCaptureMode.PushToTalk, DictationTurnEngine.OpenAiWhisper)]
    [InlineData(DictationPrimaryEngine.ElevenLabsRealtime, DictationCaptureMode.Continuous, DictationTurnEngine.ElevenLabsRealtime)]
    public void ResolveEngine_ShouldUseThePrimary(DictationPrimaryEngine primary, DictationCaptureMode mode, DictationTurnEngine expected)
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(primary, "admin-1", Now);
        setting.SetPushToTalkEngine(DictationClipEngine.Browser, "admin-1", Now);

        setting.ResolveEngine(mode).Should().Be(expected);
    }

    [Theory]
    [InlineData(DictationClipEngine.LocalWhisper, DictationTurnEngine.LocalWhisper)]
    [InlineData(DictationClipEngine.OpenAiWhisper, DictationTurnEngine.OpenAiWhisper)]
    [InlineData(DictationClipEngine.Browser, DictationTurnEngine.Browser)]
    public void ResolveEngine_ShouldUseThePushToTalkEngine_ForPushToTalkUnderElevenLabsRealtime(
        DictationClipEngine pushToTalk, DictationTurnEngine expected)
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
        setting.SetPushToTalkEngine(pushToTalk, "admin-1", Now);

        setting.ResolveEngine(DictationCaptureMode.PushToTalk).Should().Be(expected);
    }

    [Fact]
    public void Suspend_ShouldBeRefusedForLocalWhisper()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);

        var act = () => setting.Suspend(DictationPrimaryEngine.LocalWhisper, "Model missing", Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Suspend_ShouldChangeNothing_ForAnEngineNoLongerSelected()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);

        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now).Should().BeFalse();

        setting.State.Should().Be(DictationEngineState.Active);
    }

    [Fact]
    public void Suspend_ShouldRecordTheEngineReasonAndTime_AndReportTheChangeOnce()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin-1", Now);

        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now).Should().BeTrue();
        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Again", Now.AddMinutes(1)).Should().BeFalse();

        setting.State.Should().Be(DictationEngineState.Suspended);
        setting.SuspendedEngine.Should().Be(DictationPrimaryEngine.OpenAiWhisper);
        setting.SuspensionReason.Should().Be("Quota exhausted");
        setting.SuspendedAtUtc.Should().Be(Now);
        setting.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public void Suspend_ShouldTruncateALongReason()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin-1", Now);

        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, new string('x', 900), Now);

        setting.SuspensionReason.Should().HaveLength(DictationEngineSetting.MaxSuspensionReasonLength);
    }

    [Fact]
    public void Suspend_ShouldAcceptThePushToTalkEngine_UnderElevenLabsRealtime()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin-1", Now);

        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now).Should().BeTrue();

        setting.IsSuspendedFor(DictationTurnEngine.OpenAiWhisper).Should().BeTrue();
        setting.IsSuspendedFor(DictationTurnEngine.ElevenLabsRealtime).Should().BeFalse();
    }

    [Fact]
    public void IsSuspendedFor_ShouldLeaveLocalWhisperServingPushToTalk_UnderAnElevenLabsSuspension()
    {
        var setting = Suspended(DictationPrimaryEngine.ElevenLabsRealtime);

        setting.IsSuspendedFor(DictationTurnEngine.ElevenLabsRealtime).Should().BeTrue();
        setting.IsSuspendedFor(setting.ResolveEngine(DictationCaptureMode.PushToTalk)).Should().BeFalse();
        setting.IsSuspendedFor(DictationTurnEngine.Browser).Should().BeFalse();
    }

    [Fact]
    public void RevertToLocalWhisper_ShouldResetThePrimary_ForOpenAi()
    {
        var setting = Suspended(DictationPrimaryEngine.OpenAiWhisper);

        setting.RevertToLocalWhisper("openai", DictationRevertReason.VendorSwitchedOff, Now.AddHours(1)).Should().BeTrue();

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        setting.State.Should().Be(DictationEngineState.Active);
        setting.LastRevertedAtUtc.Should().Be(Now.AddHours(1));
        setting.LastRevertReason.Should().Be(DictationRevertReason.VendorSwitchedOff);
        setting.LastRevertedFrom.Should().Be(DictationPrimaryEngine.OpenAiWhisper);
    }

    [Fact]
    public void RevertToLocalWhisper_ShouldResetThePushToTalkEngine_ForOpenAi()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin-1", Now);

        setting.RevertToLocalWhisper("OpenAI", DictationRevertReason.VendorSwitchedOff, Now).Should().BeTrue();

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
        setting.PushToTalkEngine.Should().Be(DictationClipEngine.LocalWhisper);
        setting.LastRevertedFrom.Should().Be(DictationPrimaryEngine.OpenAiWhisper);
    }

    [Fact]
    public void RevertToLocalWhisper_ShouldResetThePrimary_ForElevenLabs_AndKeepAnotherVendorsSuspension()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin-1", Now);
        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin-1", Now);
        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted", Now);

        setting.RevertToLocalWhisper("elevenlabs", DictationRevertReason.VendorSwitchedOff, Now).Should().BeTrue();

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        setting.PushToTalkEngine.Should().Be(DictationClipEngine.OpenAiWhisper);
        setting.State.Should().Be(DictationEngineState.Suspended);
        setting.LastRevertedFrom.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
    }

    [Fact]
    public void RevertToLocalWhisper_ShouldBeANoOp_ForAnUnrelatedVendor()
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin-1", Now);

        setting.RevertToLocalWhisper("anthropic", DictationRevertReason.VendorSwitchedOff, Now).Should().BeFalse();

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.OpenAiWhisper);
        setting.LastRevertedAtUtc.Should().BeNull();
    }

    [Theory]
    [InlineData(DictationPrimaryEngine.LocalWhisper, DictationClipEngine.LocalWhisper, "openai", false)]
    [InlineData(DictationPrimaryEngine.OpenAiWhisper, DictationClipEngine.LocalWhisper, "openai", true)]
    [InlineData(DictationPrimaryEngine.ElevenLabsRealtime, DictationClipEngine.LocalWhisper, "elevenlabs", true)]
    [InlineData(DictationPrimaryEngine.ElevenLabsRealtime, DictationClipEngine.OpenAiWhisper, "openai", true)]
    [InlineData(DictationPrimaryEngine.ElevenLabsRealtime, DictationClipEngine.Browser, "openai", false)]
    [InlineData(DictationPrimaryEngine.OpenAiWhisper, DictationClipEngine.LocalWhisper, "", false)]
    public void DependsOnVendor_ShouldCoverThePrimaryAndThePushToTalkEngine(
        DictationPrimaryEngine primary, DictationClipEngine pushToTalk, string vendor, bool expected)
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(primary, "admin-1", Now);
        setting.SetPushToTalkEngine(pushToTalk, "admin-1", Now);

        setting.DependsOnVendor(vendor).Should().Be(expected);
    }

    private static DictationEngineSetting Suspended(DictationPrimaryEngine engine)
    {
        var setting = DictationEngineSetting.CreateDefault(Now);
        setting.SetPrimary(engine, "admin-1", Now);
        setting.Suspend(engine, "Quota exhausted", Now).Should().BeTrue();
        return setting;
    }
}
