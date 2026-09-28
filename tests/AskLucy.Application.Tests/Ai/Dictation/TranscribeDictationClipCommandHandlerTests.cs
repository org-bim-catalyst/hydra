using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;
using AskLucy.Application.OperationalFailures;
using AskLucy.Domain.Ai.Dictation;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Dictation;

/// <summary>
/// specs/078 contracts/dictation-transcription.md steps 1–4 (US1) and step 5, the failure path
/// (US2): a transcriber failure is a failover (no second transcriber to try this turn), an invalid
/// WAV clip is a validation failure, and Local Whisper never suspends.
/// </summary>
public sealed class TranscribeDictationClipCommandHandlerTests
{
    private readonly IDictationEngineSettingRepository _settings = Substitute.For<IDictationEngineSettingRepository>();
    private readonly ILocalWhisperModelCatalog _catalog = Substitute.For<ILocalWhisperModelCatalog>();
    private readonly IDictationClipTranscriber _localWhisper = Substitute.For<IDictationClipTranscriber>();
    private readonly IVoiceFailureReporter _reporter = Substitute.For<IVoiceFailureReporter>();
    private readonly IFailureClassifier _classifier = Substitute.For<IFailureClassifier>();

    public TranscribeDictationClipCommandHandlerTests()
    {
        _localWhisper.Engine.Returns(DictationClipEngine.LocalWhisper);
    }

    private TranscribeDictationClipCommandHandler CreateHandler(params IDictationClipTranscriber[] transcribers) =>
        new(_settings, _catalog, transcribers, _reporter, new DictationFailurePolicy(_classifier, _reporter));

    /// <summary>A minimal 16 kHz mono 16-bit PCM WAV clip; only the header the handler reads matters here.</summary>
    private static MemoryStream ValidWav()
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(32000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(2);
        writer.Write(new byte[2]);
        writer.Flush();
        stream.Position = 0;
        return stream;
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

    [Fact]
    public async Task ModelReady_ShouldResolveTheEngineThroughTheSetting_AndCallExactlyThatTranscriber()
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));
        _localWhisper.TranscribeAsync(Arg.Any<DictationClip>(), Arg.Any<CancellationToken>())
            .Returns(new DictationTranscript("hello lucy", "en", TimeSpan.FromMilliseconds(400)));
        var other = Substitute.For<IDictationClipTranscriber>();
        other.Engine.Returns(DictationClipEngine.OpenAiWhisper);

        var result = await CreateHandler(_localWhisper, other).Handle(
            new TranscribeDictationClipCommand(ValidWav(), "en"), CancellationToken.None);

        await _localWhisper.Received(1).TranscribeAsync(Arg.Any<DictationClip>(), Arg.Any<CancellationToken>());
        await other.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, TestContext.Current.CancellationToken);
        result.Should().BeOfType<DictationTranscriptionResult.Transcribed>()
            .Which.Should().Be(new DictationTranscriptionResult.Transcribed("hello lucy", "en"));
    }

    [Fact]
    public async Task Success_ShouldReportServed_WithTheModelFile()
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));
        _localWhisper.TranscribeAsync(Arg.Any<DictationClip>(), Arg.Any<CancellationToken>())
            .Returns(new DictationTranscript("hello lucy", null, TimeSpan.FromMilliseconds(400)));

        await CreateHandler(_localWhisper).Handle(new TranscribeDictationClipCommand(ValidWav(), "en"), CancellationToken.None);

        _reporter.Received(1).ReportServed(VoiceOperations.Transcription, new VoiceEngineIdentity("Local Whisper", null, "ggml-base.bin"));
    }

    [Theory]
    [MemberData(nameof(NotConfiguredResolutions))]
    public async Task NotConfigured_ShouldReturnUnavailable_WithNothingReported(LocalWhisperModelResolution resolution)
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>()).Returns(resolution);

        var result = await CreateHandler(_localWhisper).Handle(
            new TranscribeDictationClipCommand(ValidWav(), "en"), CancellationToken.None);

        result.Should().Be(DictationTranscriptionResult.Unavailable.Instance);
        await _localWhisper.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, TestContext.Current.CancellationToken);
        _reporter.DidNotReceiveWithAnyArgs().ReportServed(default!, default!);
    }

    public static TheoryData<LocalWhisperModelResolution> NotConfiguredResolutions() => new()
    {
        LocalWhisperModelResolution.None.Instance,
        new LocalWhisperModelResolution.Unavailable("Marked unavailable.", "whisper.cpp (ggml-base.bin)"),
    };

    [Fact]
    public async Task TranscriberThrows_ShouldReportAFailover_AndReturnUnavailable_WithNoSecondTranscriberTried()
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));
        _localWhisper.TranscribeAsync(Arg.Any<DictationClip>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("model failed to load"));

        var result = await CreateHandler(_localWhisper).Handle(
            new TranscribeDictationClipCommand(ValidWav(), "en"), CancellationToken.None);

        result.Should().Be(DictationTranscriptionResult.Unavailable.Instance);
        // The handler forwards Handle's own token (CancellationToken.None above), not the test
        // runner's TestContext token — asserting against the latter mismatched arguments here.
        _reporter.Received(1).ReportFailover(
            VoiceOperations.Transcription,
            Arg.Is<VoiceEngineIdentity>(e => e.ProviderName == "Local Whisper" && e.Model == "ggml-base.bin"),
            Arg.Any<Exception>(),
            true,
            CancellationToken.None);
        _reporter.DidNotReceiveWithAnyArgs().ReportServed(default!, default!);
    }

    [Fact]
    public async Task TranscriberThrows_ForLocalWhisper_ShouldNeverSuspendTheSetting()
    {
        var modelId = Guid.NewGuid();
        var setting = LocalWhisperPrimary(modelId);
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(setting);
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));
        _localWhisper.TranscribeAsync(Arg.Any<DictationClip>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("model failed to load"));

        await CreateHandler(_localWhisper).Handle(new TranscribeDictationClipCommand(ValidWav(), "en"), CancellationToken.None);

        setting.State.Should().NotBe(DictationEngineState.Suspended);
    }

    [Fact]
    public async Task InvalidWav_ShouldReportAValidationFailure_NotAFailover_AndThrow()
    {
        var modelId = Guid.NewGuid();
        _settings.GetOrCreateAsync(Arg.Any<CancellationToken>()).Returns(LocalWhisperPrimary(modelId));
        _catalog.ResolveSelectedAsync(modelId, Arg.Any<CancellationToken>())
            .Returns(new LocalWhisperModelResolution.Ready("/models/ggml-base.bin", "whisper.cpp (ggml-base.bin)", "ggml-base.bin"));
        var notWav = new MemoryStream("not a wav file"u8.ToArray());

        await FluentActions.Awaiting(() => CreateHandler(_localWhisper).Handle(
                new TranscribeDictationClipCommand(notWav, "en"), CancellationToken.None))
            .Should().ThrowAsync<DictationAudioInvalidException>();

        // The handler forwards Handle's own token (CancellationToken.None above), not the test
        // runner's TestContext token — asserting against the latter mismatched arguments here.
        _reporter.Received(1).ReportFailure(
            VoiceOperations.Transcription, Arg.Any<VoiceEngineIdentity>(), Arg.Any<DictationAudioInvalidException>(), CancellationToken.None);
        _reporter.DidNotReceiveWithAnyArgs().ReportFailover(default!, default!, default!, default, TestContext.Current.CancellationToken);
        await _localWhisper.DidNotReceiveWithAnyArgs().TranscribeAsync(default!, TestContext.Current.CancellationToken);
    }
}
