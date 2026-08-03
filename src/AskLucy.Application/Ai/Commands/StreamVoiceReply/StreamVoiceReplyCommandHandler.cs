using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using MediatR;

namespace AskLucy.Application.Ai.Commands.StreamVoiceReply;

/// <summary>
/// Orchestrates one voice turn: reuses <see cref="SendChatMessageCommand"/>'s existing LLM
/// streaming call (via <see cref="ISender"/>, not a duplicated provider-resolution path),
/// buffers the growing text into sentence-sized chunks, and feeds each completed sentence to
/// <see cref="ITextToSpeechProvider"/> as soon as it's ready — so synthesis of the first
/// sentence begins without waiting for the LLM's full reply (FR-008/SC-001).
///
/// Deliberately processes one sentence's synthesis at a time (awaited in order) rather than
/// overlapping sentence N's TTS call with sentence N+1's LLM generation — the simpler design
/// that still satisfies "begin playback before the full response finishes generating," without
/// the added complexity of a producer/consumer channel pipelining both operations
/// concurrently. Revisit only if SC-001 measurements (tasks.md T083) show this sequential
/// hand-off doesn't leave enough headroom.
///
/// A TTS-specific failure disables audio for the remainder of this turn (one
/// <see cref="AskLucy.Application.Ai.VoiceReplyEvent.AudioFailedEvent"/>, one failover record)
/// but never stops the underlying text stream — FR-033's fallback is a client-side concern
/// once it sees `audio-failed`; the text reply itself always completes normally.
/// </summary>
public sealed partial class StreamVoiceReplyCommandHandler(
    ISender mediator,
    ITextToSpeechProvider textToSpeechProvider,
    IVoiceProviderHealthRecorder healthRecorder,
    IUserVoicePreferenceRepository voicePreferences,
    ICurrentUserAccessor currentUser) : IStreamRequestHandler<StreamVoiceReplyCommand, VoiceReplyEvent>
{
    public async IAsyncEnumerable<VoiceReplyEvent> Handle(
        StreamVoiceReplyCommand request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var settings = await ResolveVoiceSettingsAsync(userId, request.Language, cancellationToken);

        yield return VoiceReplyEvent.ProviderStatusEvent("primary");

        var sentenceBuffer = new StringBuilder();
        var sequenceCounter = new SequenceCounter();
        var ttsFailed = false;

        await foreach (var chunk in mediator.CreateStream(
            new SendChatMessageCommand(request.Messages, request.ProviderId, request.ModelId, request.GenerationParameters),
            cancellationToken))
        {
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                yield return VoiceReplyEvent.TranscriptDeltaEvent(chunk.ContentDelta);
                sentenceBuffer.Append(chunk.ContentDelta);

                while (!ttsFailed && TryExtractSentence(sentenceBuffer, out var sentence))
                {
                    await foreach (var audioEvent in SynthesizeSentenceAsync(sentence, settings, sequenceCounter, userId, cancellationToken))
                    {
                        if (audioEvent.Type == "audio-failed")
                        {
                            ttsFailed = true;
                        }

                        yield return audioEvent;
                    }
                }
            }

            if (chunk.Usage is not null)
            {
                yield return VoiceReplyEvent.UsageEvent(chunk.Usage);
            }
        }

        // Flush whatever text never reached a sentence-ending punctuation mark.
        if (!ttsFailed && sentenceBuffer.Length > 0)
        {
            await foreach (var audioEvent in SynthesizeSentenceAsync(sentenceBuffer.ToString(), settings, sequenceCounter, userId, cancellationToken))
            {
                yield return audioEvent;
            }
        }

        yield return VoiceReplyEvent.DoneEvent();
    }

    /// <summary>A running audio-chunk sequence number across the *entire* reply, not per
    /// sentence — contracts/voice-reply-stream.md's `sequence` field is meant to let the
    /// client detect gaps across the whole stream. A plain mutable holder (rather than a
    /// `ref int` parameter, which C# iterator methods cannot capture) shared across each
    /// sentence's <see cref="SynthesizeSentenceAsync"/> call.</summary>
    private sealed class SequenceCounter
    {
        public int Next() => Value++;

        private int Value;
    }

    private async IAsyncEnumerable<VoiceReplyEvent> SynthesizeSentenceAsync(
        string sentence,
        VoiceSettingsDto settings,
        SequenceCounter sequenceCounter,
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            yield break;
        }

        IAsyncEnumerator<byte[]> enumerator;
        Exception? startFailure = null;
        try
        {
            enumerator = textToSpeechProvider.StreamSpeechAsync(sentence, settings, cancellationToken).GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
        {
            startFailure = ex;
            enumerator = EmptyAsyncEnumerator();
        }

        if (startFailure is not null)
        {
            await healthRecorder.RecordFailoverAsync(userId, Truncate(startFailure.Message), cancellationToken);
            yield return VoiceReplyEvent.AudioFailedEvent();
            yield break;
        }

        await using (enumerator)
        {
            while (true)
            {
                bool hasNext;
                Exception? moveNextFailure = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (ex is AiProviderUnavailableException or AiProviderRateLimitedException or AiProviderAuthenticationException)
                {
                    hasNext = false;
                    moveNextFailure = ex;
                }

                if (moveNextFailure is not null)
                {
                    await healthRecorder.RecordFailoverAsync(userId, Truncate(moveNextFailure.Message), cancellationToken);
                    yield return VoiceReplyEvent.AudioFailedEvent();
                    yield break;
                }

                if (!hasNext)
                {
                    yield break;
                }

                yield return VoiceReplyEvent.AudioChunkEvent(sequenceCounter.Next(), enumerator.Current);
            }
        }
    }

    private static async IAsyncEnumerator<byte[]> EmptyAsyncEnumerator()
    {
        await Task.CompletedTask;
        yield break;
    }

    private async Task<VoiceSettingsDto> ResolveVoiceSettingsAsync(string userId, string language, CancellationToken cancellationToken)
    {
        var defaults = textToSpeechProvider.ResolveDefaultSettings(language);
        var preference = await voicePreferences.GetByUserIdAsync(userId, cancellationToken);

        if (preference is null)
        {
            return defaults;
        }

        return defaults with
        {
            VoiceId = preference.SelectedVoiceId ?? defaults.VoiceId,
            Speed = preference.VoiceSpeed ?? defaults.Speed,
            Style = preference.VoiceStyle ?? defaults.Style,
        };
    }

    private static string Truncate(string message) => message.Length > 500 ? message[..500] : message;

    /// <summary>Matches a sentence-ending punctuation mark followed by whitespace or the end
    /// of the buffer — the simplest boundary that keeps TTS chunks natural-sounding without a
    /// full NLP sentence splitter (constitution §III YAGNI).</summary>
    private static bool TryExtractSentence(StringBuilder buffer, out string sentence)
    {
        var text = buffer.ToString();
        var match = SentenceBoundary().Match(text);
        if (!match.Success)
        {
            sentence = string.Empty;
            return false;
        }

        var endIndex = match.Index + match.Length;
        sentence = text[..endIndex].Trim();
        buffer.Remove(0, endIndex);
        return true;
    }

    [GeneratedRegex(@"[.!?]+(\s|$)")]
    private static partial Regex SentenceBoundary();
}
