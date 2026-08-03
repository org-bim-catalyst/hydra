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

    private void SetUpLlmStream(params StreamChunk[] chunks) =>
        _mediator.CreateStream(Arg.Any<SendChatMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable(chunks));

    [Fact]
    public async Task Handle_ShouldSplitGrowingTextIntoSentences_AndSynthesizeEachWithContinuingSequence()
    {
        SetUpLlmStream(new StreamChunk("Hello world. "), new StreamChunk("How are you?"));

        _textToSpeech
            .StreamSpeechAsync("Hello world.", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[1, 2], [3, 4]]));
        _textToSpeech
            .StreamSpeechAsync("How are you?", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable<byte[]>([[5, 6]]));

        var events = await CollectAsync(new StreamVoiceReplyCommand([], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

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
    public async Task Handle_ShouldEmitAudioFailed_AndRecordFailover_ButStillCompleteTheTextStream_WhenTtsFails()
    {
        SetUpLlmStream(new StreamChunk("First sentence. "), new StreamChunk("Second sentence."));

        _textToSpeech
            .StreamSpeechAsync("First sentence.", DefaultSettings, Arg.Any<CancellationToken>())
            .Returns(_ => throw new AiProviderUnavailableException("ElevenLabs TTS is down."));

        var events = await CollectAsync(new StreamVoiceReplyCommand([], Guid.NewGuid(), Guid.NewGuid(), null, "en"));

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
