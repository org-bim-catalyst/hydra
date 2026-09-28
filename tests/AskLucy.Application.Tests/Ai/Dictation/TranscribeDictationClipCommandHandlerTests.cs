using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Application.Ai.Dictation.Commands.TranscribeDictationClip;
using AskLucy.Domain.Ai.Dictation;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai.Dictation;

/// <summary>
/// specs/078 contracts/dictation-transcription.md steps 1–4 (US1). Step 5, the failure path, is
/// covered by the extension in US2.
/// </summary>
public sealed class TranscribeDictationClipCommandHandlerTests
{
    private readonly IDictationEngineSettingRepository _settings = Substitute.For<IDictationEngineSettingRepository>();
    private readonly ILocalWhisperModelCatalog _catalog = Substitute.For<ILocalWhisperModelCatalog>();
    private readonly IDictationClipTranscriber _localWhisper = Substitute.For<IDictationClipTranscriber>();
    private readonly IVoiceFailureReporter _reporter = Substitute.For<IVoiceFailureReporter>();

    public TranscribeDictationClipCommandHandlerTests()
    {
        _localWhisper.Engine.Returns(DictationClipEngine.LocalWhisper);
    }

    private TranscribeDictationClipCommandHandler CreateHandler(params IDictationClipTranscriber[] transcribers) =>
        new(_settings, _catalog, transcribers, _reporter);

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
}
