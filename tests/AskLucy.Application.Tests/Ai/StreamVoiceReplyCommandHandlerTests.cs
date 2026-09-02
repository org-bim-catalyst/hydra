using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Ai.Commands.StreamVoiceReply;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// tasks.md T034 — <see cref="ISender"/> (for the reused LLM stream) and
/// <see cref="ITextToSpeechProvider"/> are faked, proving: (1) growing LLM text is split into
/// sentence-sized chunks and each is synthesized in turn with a continuously-incrementing
/// audio sequence number, and (2) a TTS-specific failure mid-stream records a failover, emits
/// `audio-failed`, and still lets the underlying text stream complete normally (FR-033).
/// </summary>
public sealed class StreamVoiceReplyCommandHandlerTests
{
    private static readonly VoiceSettingsDto DefaultSettings = new(
        VoiceId: "default-voice", ModelId: "eleven_v3", Stability: 0.5, SimilarityBoost: 0.75,
        Style: 0.0, Speed: 1.0, UseSpeakerBoost: true, OutputFormat: "mp3_44100_128");

    private readonly ISender _mediator = Substitute.For<ISender>();
    private readonly ITextToSpeechProvider _textToSpeech = Substitute.For<ITextToSpeechProvider>();
    private readonly IVoiceProviderHealthRecorder _healthRecorder = Substitute.For<IVoiceProviderHealthRecorder>();
    private readonly IUserVoicePreferenceRepository _voicePreferences = Substitute.For<IUserVoicePreferenceRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly StreamVoiceReplyCommandHandler _handler;

    public StreamVoiceReplyCommandHandlerTests()
    {
        _currentUser.UserId.Returns("user-1");
        _voicePreferences.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns((Domain.Ai.UserVoicePreference?)null);
        _textToSpeech.ResolveDefaultSettings(Arg.Any<string>()).Returns(DefaultSettings);

        _handler = new StreamVoiceReplyCommandHandler(_mediator, _textToSpeech, _healthRecorder, _voicePreferences, _currentUser);
    }

    private void SetUpLlmStream(params ChatStreamChunk[] chunks) =>
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(chunks));

    [Fact]
    public async Task Handle_ShouldSplitGrowingTextIntoSentences_AndSynthesizeEachWithContinuingSequence()
    {
        SetUpLlmStream(new ChatStreamChunk("Hello world. ", null), new ChatStreamChunk("How are you?", null));

        _textToSpeech
            .StreamSpeechAsync("Hello world.", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[1, 2], [3, 4]]));
        _textToSpeech
            .StreamSpeechAsync("How are you?", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[5, 6]]));

        var events = await CollectAsync(new StreamVoiceReplyCommand(Guid.NewGuid(), [], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

        events.Where(e => e.Type == "transcript-delta").Select(e => e.TranscriptDelta)
            .Should().Equal("Hello world. ", "How are you?");

        var audioChunks = events.Where(e => e.Type == "audio-chunk").ToList();
        audioChunks.Should().HaveCount(3);
        audioChunks.Select(e => e.AudioSequence).Should().Equal(0, 1, 2);
        audioChunks.Select(e => Convert.ToBase64String(e.AudioBytes!)).Should().Equal(
            Convert.ToBase64String([1, 2]), Convert.ToBase64String([3, 4]), Convert.ToBase64String([5, 6]));

        events.Last().Type.Should().Be("done");
        events.First().Type.Should().Be("provider-status");
    }

    [Fact]
    public async Task Handle_ShouldSpeakEachMarkdownLine_WithoutWaitingForTerminalPunctuation()
    {
        // Replies are markdown. Headings and bullet items routinely carry no full stop, so on
        // terminal punctuation alone the buffer ran through the whole list before one sentence
        // could be synthesised — which is why Lucy started speaking long after the text had
        // finished rendering. A line is a natural unit of speech in its own right.
        SetUpLlmStream(new ChatStreamChunk("### Facilities\n- Walking tracks\n- Tennis courts\n", null));

        _textToSpeech.StreamSpeechAsync(Arg.Any<string>(), DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[1]]));

        await CollectAsync(new StreamVoiceReplyCommand(Guid.NewGuid(), [], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

        _textToSpeech.Received().StreamSpeechAsync("### Facilities", DefaultSettings, Arg.Any<CancellationToken>());
        _textToSpeech.Received().StreamSpeechAsync("- Walking tracks", DefaultSettings, Arg.Any<CancellationToken>());
        _textToSpeech.Received().StreamSpeechAsync("- Tennis courts", DefaultSettings, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldFlushALongUnpunctuatedRun_RatherThanHoldItUntilItEnds()
    {
        // No boundary of any kind for far longer than anyone should wait in silence.
        var runOn = string.Join(' ', Enumerable.Repeat("word", 60));
        SetUpLlmStream(new ChatStreamChunk(runOn, null));

        _textToSpeech.StreamSpeechAsync(Arg.Any<string>(), DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[1]]));

        await CollectAsync(new StreamVoiceReplyCommand(Guid.NewGuid(), [], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

        var spoken = _textToSpeech.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ITextToSpeechProvider.StreamSpeechAsync))
            .Select(c => (string)c.GetArguments()[0]!)
            .ToList();

        spoken.Should().NotBeEmpty();
        spoken[0].Length.Should().BeLessThan(runOn.Length, "the first utterance must not wait for the whole run");
        // Broken on a word boundary, never mid-word.
        spoken[0].Should().NotEndWith("wor");
    }

    [Fact]
    public async Task Handle_ShouldEmitAudioFailed_AndRecordFailover_ButStillCompleteTheTextStream_WhenTtsFails()
    {
        SetUpLlmStream(new ChatStreamChunk("First sentence. ", null), new ChatStreamChunk("Second sentence.", null));

        _textToSpeech
            .StreamSpeechAsync("First sentence.", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiProviderUnavailableException("ElevenLabs TTS is down."));

        var events = await CollectAsync(new StreamVoiceReplyCommand(Guid.NewGuid(), [], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

        events.Should().ContainSingle(e => e.Type == "audio-failed");
        events.Where(e => e.Type == "transcript-delta").Select(e => e.TranscriptDelta)
            .Should().Equal("First sentence. ", "Second sentence.");
        events.Should().NotContain(e => e.Type == "audio-chunk");
        events.Last().Type.Should().Be("done");

        await _healthRecorder.Received(1).RecordFailoverAsync("user-1", Arg.Any<string>(), Arg.Any<CancellationToken>());
        // The second sentence must not attempt TTS at all once the turn's audio has failed.
        _textToSpeech.DidNotReceive().StreamSpeechAsync("Second sentence.", Arg.Any<VoiceSettingsDto>(), Arg.Any<CancellationToken>());
    }

    private async Task<List<VoiceReplyEvent>> CollectAsync(StreamVoiceReplyCommand command)
    {
        var events = new List<VoiceReplyEvent>();
        await foreach (var voiceEvent in _handler.Handle(command, CancellationToken.None))
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
