using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.CreateSpeechToTextSession;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;
using FluentAssertions;
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

    private CreateSpeechToTextSessionCommandHandler CreateHandler() =>
        new(_sessionProvider, _healthRecorder, _failoverEvents, _reporter, _currentUser, _settings, _catalog);

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

    [Fact]
    public async Task ModeAbsent_ShouldDefaultToContinuous()
    {
        _catalog.ResolveSelectedAsync(null, Arg.Any<CancellationToken>()).Returns(LocalWhisperModelResolution.None.Instance);

        var command = new CreateSpeechToTextSessionCommand("en");

        command.Mode.Should().Be(DictationCaptureMode.Continuous);
        var session = await CreateHandler().Handle(command, CancellationToken.None);
        session.Should().Be(DictationSession.Browser(degraded: false));
    }
}
