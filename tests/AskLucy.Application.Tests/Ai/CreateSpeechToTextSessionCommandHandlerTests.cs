using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/078 research D6 — the Local Whisper and Browser rows of the dictation-session table
/// (contracts/dictation-session.md). specs/074 US2 — a failed session is a failover on the
/// operational failure trail; the next minted session is its recovery.
/// </summary>
public sealed class CreateSpeechToTextSessionCommandHandlerTests
{
    private readonly ISpeechToTextSessionProvider _sessionProvider = Substitute.For<ISpeechToTextSessionProvider>();
    private readonly IVoiceProviderHealthRecorder _healthRecorder = Substitute.For<IVoiceProviderHealthRecorder>();
    private readonly IVoiceProviderFailoverEventRepository _failoverEvents = Substitute.For<IVoiceProviderFailoverEventRepository>();
    private readonly IVoiceFailureReporter _reporter = Substitute.For<IVoiceFailureReporter>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IDictationEngineSettingRepository _settings = Substitute.For<IDictationEngineSettingRepository>();
    private readonly ILocalWhisperModelCatalog _catalog = Substitute.For<ILocalWhisperModelCatalog>();
    private readonly IFailureClassifier _classifier = Substitute.For<IFailureClassifier>();
    private readonly IAIProviderRepository _aiProviders = Substitute.For<IAIProviderRepository>();
    private readonly IAiCredentialProtector _credentialProtector = Substitute.For<IAiCredentialProtector>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public CreateSpeechToTextSessionCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _sessionProvider.ProviderName.Returns("ElevenLabs");
        _sessionProvider.IsSwitchedOnAsync(Arg.Any<CancellationToken>()).Returns(true);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary());
    }

    private static DictationEngineSetting LocalWhisperPrimary(Guid? modelId = null)
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        if (modelId is { } id)
        {
            setting.SelectLocalWhisperModel(id, "admin", DateTime.UtcNow);
        }

        return setting;
    }

    private static DictationEngineSetting SettingWithPrimary(DictationPrimaryEngine engine, DictationClipEngine? pushToTalk = null)
    {
        var setting = DictationEngineSetting.CreateDefault(DateTime.UtcNow);
        setting.SetPrimary(engine, "admin", DateTime.UtcNow);
        if (pushToTalk is { } ptt)
        {
            setting.SetPushToTalkEngine(ptt, "admin", DateTime.UtcNow);
        }

        return setting;
    }

    private CreateSpeechToTextSessionCommandHandler CreateHandler() =>
        new(_sessionProvider, _healthRecorder, _failoverEvents, _reporter, _currentUser, _settings, _catalog,
            new DictationVendorGate(_aiProviders, _sessionProvider, _credentialProtector),
            new DictationFailurePolicy(_classifier, _reporter, TimeProvider.System, NullLogger<DictationFailurePolicy>.Instance),
            _unitOfWork);

    [Theory]
    [InlineData(DictationCaptureMode.Continuous)]
    [InlineData(DictationCaptureMode.PushToTalk)]
    public async Task NoModelSelected_ShouldServeTheBrowserBuiltIn_NotDegraded(DictationCaptureMode mode)
    {
        _catalog.ResolveSelectedAsync(null, Arg.Any<CancellationToken>()).Returns(LocalWhisperModelResolution.None.Instance);

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en", mode), CancellationToken.None);

        session.Should().Be(DictationSession.Browser(degraded: false));
        _reporter.DidNotReceiveWithAnyArgs().ReportFailover(default!, default!, default!, default, TestContext.Current.CancellationToken);
        await _healthRecorder.DidNotReceiveWithAnyArgs().RecordFailoverAsync(default!, default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DictationCaptureMode.Continuous)]
    [InlineData(DictationCaptureMode.PushToTalk)]
    public async Task ModelUnavailable_ShouldServeTheBrowserBuiltIn_NotDegraded(DictationCaptureMode mode)
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Unavailable("Marked unavailable.", "whisper.cpp (ggml-base.bin)"));

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en", mode), CancellationToken.None);

        session.Should().Be(DictationSession.Browser(degraded: false));
        _reporter.DidNotReceiveWithAnyArgs().ReportFailover(default!, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DictationCaptureMode.Continuous)]
    [InlineData(DictationCaptureMode.PushToTalk)]
    public async Task ModelReady_ShouldServeAClip(DictationCaptureMode mode)
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en", mode), CancellationToken.None);

        session.Should().Be(DictationSession.Clip);
        await _sessionProvider.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(DictationCaptureMode.Continuous)]
    [InlineData(DictationCaptureMode.PushToTalk)]
    public async Task ModelBroken_ShouldReportAFailoverAndServeTheBrowserDegraded(DictationCaptureMode mode)
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Broken("File missing.", "whisper.cpp (ggml-base.bin)"));

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en", mode), CancellationToken.None);

        session.Should().Be(DictationSession.Browser(degraded: true));
        // The handler forwards Handle's own token (CancellationToken.None above), not the test
        // runner's TestContext token — asserting against the latter mismatched arguments here.
        _reporter.Received(1).ReportFailover(
            VoiceOperations.Transcription,
            Arg.Is<VoiceEngineIdentity>(e => e.ProviderName == "Local Whisper" && e.Model == "whisper.cpp (ggml-base.bin)"),
            Arg.Any<Exception>(),
            true,
            CancellationToken.None);
    }

    [Fact]
    public async Task ModeAbsent_ShouldDefaultToContinuous()
    {
        _catalog.ResolveSelectedAsync(null, Arg.Any<CancellationToken>()).Returns(LocalWhisperModelResolution.None.Instance);

        var command = new CreateSpeechToTextSessionCommand("en");

        command.Mode.Should().Be(DictationCaptureMode.Continuous);
        var session = await CreateHandler().Handle(command, CancellationToken.None);
        session.Should().Be(DictationSession.Browser(degraded: false));
    }

    [Fact]
    public async Task OpenAiWhisperPrimary_SwitchedOnAndHealthy_ShouldServeAClip()
    {
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(SettingWithPrimary(DictationPrimaryEngine.OpenAiWhisper));
        var provider = AIProvider.Create("openai", "OpenAI", "system");
        provider.SetCredential("ciphertext", null, "system");
        provider.Enable("system");
        _aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, Arg.Any<CancellationToken>()).Returns(provider);

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

        session.Should().Be(DictationSession.Clip);
        _reporter.DidNotReceiveWithAnyArgs().ReportFailover(default!, default!, default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenAiWhisperPrimary_SwitchedOff_ShouldReportAFailoverAndServeTheBrowserDegraded()
    {
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(SettingWithPrimary(DictationPrimaryEngine.OpenAiWhisper));
        _aiProviders.GetByKeyAsync(DictationEngineSetting.OpenAiVendorKey, Arg.Any<CancellationToken>()).Returns((AIProvider?)null);

        var session = await CreateHandler().Handle(new CreateSpeechToTextSessionCommand("en"), CancellationToken.None);

        session.Should().Be(DictationSession.Browser(degraded: true));
        _reporter.Received(1).ReportFailover(
            VoiceOperations.Transcription, Arg.Is<VoiceEngineIdentity>(e => e.ProviderName == "OpenAI Whisper"), Arg.Any<Exception>(), true, CancellationToken.None);
    }

    [Fact]
    public async Task ElevenLabsRealtimePrimary_Continuous_MintSucceeds_ShouldServeRealtime()
    {
        var setting = SettingWithPrimary(DictationPrimaryEngine.ElevenLabsRealtime);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).Returns(new SpeechToTextSession("token-1", DateTime.UtcNow.AddMinutes(5)));

        var session = await CreateHandler().Handle(
            new CreateSpeechToTextSessionCommand("en", DictationCaptureMode.Continuous), CancellationToken.None);

        session.Should().Be(DictationSession.Realtime("token-1", session.ExpiresAtUtc!.Value));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ElevenLabsRealtimePrimary_PushToTalk_ShouldServeThePushToTalkEngineInstead()
    {
        var setting = SettingWithPrimary(DictationPrimaryEngine.ElevenLabsRealtime, DictationClipEngine.LocalWhisper);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        var modelId = Guid.NewGuid();
        setting.SelectLocalWhisperModel(modelId, "admin", DateTime.UtcNow);
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));

        var session = await CreateHandler().Handle(
            new CreateSpeechToTextSessionCommand("en", DictationCaptureMode.PushToTalk), CancellationToken.None);

        session.Should().Be(DictationSession.Clip);
        await _sessionProvider.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ElevenLabsRealtimePrimary_CriticalMintFailure_ShouldSuspendAndCommit()
    {
        var setting = SettingWithPrimary(DictationPrimaryEngine.ElevenLabsRealtime);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        var failure = new AiProviderQuotaExhaustedException("Quota exhausted.");
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _classifier.Classify(Arg.Any<Exception>(), Arg.Any<CancellationToken>()).Returns(OperationalFailureKind.QuotaExhausted);

        var act = async () => await CreateHandler().Handle(
            new CreateSpeechToTextSessionCommand("en", DictationCaptureMode.Continuous), CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderQuotaExhaustedException>();
        setting.State.Should().Be(DictationEngineState.Suspended);
        setting.SuspendedEngine.Should().Be(DictationPrimaryEngine.ElevenLabsRealtime);
        await _unitOfWork.Received(1).SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ElevenLabsRealtimePrimary_TransientMintFailure_ShouldNotSuspend()
    {
        var setting = SettingWithPrimary(DictationPrimaryEngine.ElevenLabsRealtime);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        var failure = new AiProviderUnavailableException("Temporarily unavailable.");
        _sessionProvider.CreateSessionAsync("en", Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _classifier.Classify(Arg.Any<Exception>(), Arg.Any<CancellationToken>()).Returns(OperationalFailureKind.DependencyUnreachable);

        var act = async () => await CreateHandler().Handle(
            new CreateSpeechToTextSessionCommand("en", DictationCaptureMode.Continuous), CancellationToken.None);

        await act.Should().ThrowAsync<AiProviderUnavailableException>();
        setting.State.Should().Be(DictationEngineState.Active);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SuspendedForElevenLabs_ShouldServeTheBrowserDegraded_WithNoVendorCall()
    {
        var setting = SettingWithPrimary(DictationPrimaryEngine.ElevenLabsRealtime);
        setting.Suspend(DictationPrimaryEngine.ElevenLabsRealtime, "Quota exhausted.", DateTime.UtcNow);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);

        var session = await CreateHandler().Handle(
            new CreateSpeechToTextSessionCommand("en", DictationCaptureMode.Continuous), CancellationToken.None);

        session.Should().Be(DictationSession.Browser(degraded: true));
        await _sessionProvider.DidNotReceiveWithAnyArgs().CreateSessionAsync(default!, TestContext.Current.CancellationToken);
        await _sessionProvider.DidNotReceiveWithAnyArgs().IsSwitchedOnAsync(TestContext.Current.CancellationToken);
    }
}
