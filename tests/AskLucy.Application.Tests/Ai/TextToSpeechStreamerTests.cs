using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// A chunk that is emoji/punctuation-only passes <see cref="string.IsNullOrWhiteSpace"/> but has
/// nothing ElevenLabs considers speakable once it strips emojis/speaker tags — it 400s with
/// "input_text_empty" and would otherwise fail (and fail over) the entire reply's audio for a
/// single stray emoji sentence. <see cref="TextToSpeechStreamer.StreamAsync"/> must skip such
/// chunks before ever calling the provider.
/// </summary>
public sealed class TextToSpeechStreamerTests
{
    private static readonly VoiceSettingsDto DefaultSettings = new(
        VoiceId: "default-voice", ModelId: "eleven_v3", Stability: 0.5, SimilarityBoost: 0.75,
        Style: 0.0, Speed: 1.0, UseSpeakerBoost: true, OutputFormat: "mp3_44100_128");

    private readonly ITextToSpeechProvider _textToSpeech = Substitute.For<ITextToSpeechProvider>();
    private readonly IVoiceProviderHealthRecorder _healthRecorder = Substitute.For<IVoiceProviderHealthRecorder>();

    [Theory]
    [InlineData("😊")]
    [InlineData("!?!?")]
    [InlineData("   ")]
    [InlineData("---")]
    public async Task StreamAsync_ShouldSkipTheProviderEntirely_WhenTextHasNoSpeakableContent(string text)
    {
        var events = await CollectAsync(text);

        events.Should().BeEmpty();
        _ = _textToSpeech.DidNotReceive().StreamSpeechAsync(Arg.Any<string>(), Arg.Any<VoiceSettingsDto>(), Arg.Any<CancellationToken>());
        await _healthRecorder.DidNotReceive().RecordFailoverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StreamAsync_ShouldStillCallTheProvider_WhenTextHasAtLeastOneLetterOrDigit()
    {
        _textToSpeech
            .StreamSpeechAsync("Hello 😊", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[1, 2]]));

        var events = await CollectAsync("Hello 😊");

        events.Should().ContainSingle(e => e.Type == "audio-chunk");
        _ = _textToSpeech.Received(1).StreamSpeechAsync("Hello 😊", DefaultSettings, Arg.Any<CancellationToken>());
    }

    private async Task<List<VoiceReplyEvent>> CollectAsync(string text)
    {
        var events = new List<VoiceReplyEvent>();
        await foreach (var voiceEvent in TextToSpeechStreamer.StreamAsync(
            _textToSpeech, _healthRecorder, text, DefaultSettings, () => 0, "user-1", CancellationToken.None))
        {
            events.Add(voiceEvent);
        }

        return events;
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
