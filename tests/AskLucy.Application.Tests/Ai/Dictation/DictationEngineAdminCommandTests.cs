using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.Ai.Dictation.Commands.SetDictationPrimaryEngine;
using AskLucy.Application.Ai.Dictation.Commands.SetPushToTalkEngine;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Ai.Dictation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Dictation;

/// <summary>
/// specs/078 FR-004/FR-015/FR-017/FR-018 — admin control of the primary and Push-to-Talk dictation
/// engines: a switched-off vendor can't be chosen, and switching a vendor off reverts any dependent
/// choice back to Local Whisper.
/// </summary>
public sealed class DictationEngineAdminCommandTests
{
    private readonly IDictationEngineSettingRepository _settings = Substitute.For<IDictationEngineSettingRepository>();
    private readonly IAIProviderRepository _aiProviders = Substitute.For<IAIProviderRepository>();
    private readonly ISpeechToTextSessionProvider _elevenLabs = Substitute.For<ISpeechToTextSessionProvider>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public DictationEngineAdminCommandTests()
    {
        _currentUser.UserId.Returns("admin-user");
    }

    private DictationVendorGate CreateVendorGate() => new(_aiProviders, _elevenLabs, _credentialProtector);

    private SetDictationPrimaryEngineCommandHandler CreatePrimaryHandler() => new(
        _settings, CreateVendorGate(), _unitOfWork, _currentUser, _timeProvider,
        NullLogger<SetDictationPrimaryEngineCommandHandler>.Instance);

    private SetPushToTalkEngineCommandHandler CreatePushToTalkHandler() => new(
        _settings, CreateVendorGate(), _unitOfWork, _currentUser, _timeProvider,
        NullLogger<SetPushToTalkEngineCommandHandler>.Instance);

    private static AIProvider EnabledProvider(string providerKey)
    {
        var provider = AIProvider.Create(providerKey, providerKey, "system");
        provider.SetCredential("ciphertext", null, "system");
        provider.Enable("system");
        return provider;
    }

    private static string RowVersionOf(DictationEngineSetting setting) => Convert.ToBase64String(setting.RowVersion);

    [Fact]
    public async Task SetPrimary_OpenAiWhisper_WhileOpenAiIsOff_ShouldThrow_AndNotSave()
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        _aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, Arg.Any<CancellationToken>())
            .Returns((AIProvider?)null);

        var command = new SetDictationPrimaryEngineCommand(DictationPrimaryEngine.OpenAiWhisper, RowVersionOf(setting));

        await FluentActions.Awaiting(() => CreatePrimaryHandler().Handle(command, CancellationToken.None))
            .Should().ThrowAsync<DictationEngineNotSelectableException>()
            .WithMessage(DictationVendorGate.OpenAiSwitchedOff);

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimary_OpenAiWhisper_WhileOpenAiIsOn_ShouldSucceed_AndClearASuspension()
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin", DateTime.UtcNow);
        setting.Suspend(DictationPrimaryEngine.OpenAiWhisper, "Quota exhausted.", DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        _aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, Arg.Any<CancellationToken>())
            .Returns(EnabledProvider(DictationEngineSetting.OpenAiVendorKey));

        var command = new SetDictationPrimaryEngineCommand(DictationPrimaryEngine.OpenAiWhisper, RowVersionOf(setting));
        await CreatePrimaryHandler().Handle(command, CancellationToken.None);

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.OpenAiWhisper);
        setting.State.Should().Be(DictationEngineState.Active);
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SetPushToTalkEngine_OpenAiWhisper_WhileOpenAiIsOff_ShouldThrow()
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin", DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        _aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, Arg.Any<CancellationToken>())
            .Returns((AIProvider?)null);

        var command = new SetPushToTalkEngineCommand(DictationClipEngine.OpenAiWhisper, RowVersionOf(setting));

        await FluentActions.Awaiting(() => CreatePushToTalkHandler().Handle(command, CancellationToken.None))
            .Should().ThrowAsync<DictationEngineNotSelectableException>()
            .WithMessage(DictationVendorGate.OpenAiSwitchedOff);

        setting.PushToTalkEngine.Should().Be(DictationClipEngine.LocalWhisper);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchOffObserver_RevertsPrimaryEngine_ToLocalWhisper()
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        setting.SetPrimary(DictationPrimaryEngine.OpenAiWhisper, "admin", DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        var observer = new DictationEngineSettingSwitchOffObserver(_settings);

        await observer.OnSwitchedOffAsync(DictationEngineSetting.OpenAiVendorKey, DateTime.UtcNow, CancellationToken.None);

        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.LocalWhisper);
        setting.LastRevertReason.Should().Be(DictationRevertReason.VendorSwitchedOff);
    }

    [Fact]
    public async Task SwitchOffObserver_RevertsPushToTalkEngine_ToLocalWhisper()
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        setting.SetPrimary(DictationPrimaryEngine.ElevenLabsRealtime, "admin", DateTime.UtcNow);
        setting.SetPushToTalkEngine(DictationClipEngine.OpenAiWhisper, "admin", DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        var observer = new DictationEngineSettingSwitchOffObserver(_settings);

        await observer.OnSwitchedOffAsync(DictationEngineSetting.OpenAiVendorKey, DateTime.UtcNow, CancellationToken.None);

        setting.PushToTalkEngine.Should().Be(DictationClipEngine.LocalWhisper);
        setting.PrimaryEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
    }
}
