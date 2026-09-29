using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Dictation;
using AskLucy.Domain.Ai.Dictation;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>specs/078 research D4 — the OpenAI Whisper clip engine delegates to the same `whisper-1` call the file-attach upload uses, and surfaces provider exceptions unchanged for the caller's own classification.</summary>
public sealed class OpenAiWhisperClipTranscriberTests
{
    private readonly IAIProvider _openAi = Substitute.For<IAIProvider>();

    [Fact]
    public void Engine_ShouldBeOpenAiWhisper()
    {
        new OpenAiWhisperClipTranscriber(_openAi).Engine.Should().Be(DictationClipEngine.OpenAiWhisper);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldDelegate_WithTheWhisperOneWavRequest()
    {
        _openAi.TranscribeAudioAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns("hello lucy");
        var wav = new MemoryStream([1, 2, 3]);
        var clip = new DictationClip(wav, "en");

        var transcript = await new OpenAiWhisperClipTranscriber(_openAi).TranscribeAsync(clip, TestContext.Current.CancellationToken);

        transcript.Text.Should().Be("hello lucy");
        transcript.DetectedLanguage.Should().Be("en");
        await _openAi.Received(1).TranscribeAudioAsync(wav, "clip.wav", "audio/wav", "en", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TranscribeAsync_ShouldSurfaceProviderExceptions_Unchanged()
    {
        var failure = new AiProviderQuotaExhaustedException("Quota exhausted.");
        _openAi.TranscribeAudioAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);

        var act = () => new OpenAiWhisperClipTranscriber(_openAi).TranscribeAsync(
            new DictationClip(new MemoryStream(), null), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<AiProviderQuotaExhaustedException>()).Which.Should().BeSameAs(failure);
    }
}
