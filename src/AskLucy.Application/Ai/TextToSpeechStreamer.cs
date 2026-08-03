using System.Runtime.CompilerServices;
using AskLucy.Application.Abstractions;

namespace AskLucy.Application.Ai;

/// <summary>
/// Shared per-utterance TTS streaming + failover logic (research.md Decision 5), extracted so
/// <see cref="Commands.StreamVoiceReply.StreamVoiceReplyCommandHandler"/> (sentence-by-sentence,
/// interleaved with an in-progress LLM stream) and
/// <see cref="Commands.SynthesizeSpeech.SynthesizeSpeechCommandHandler"/> (a complete,
/// already-known text — the "speak this reply aloud" path, FR-006) don't duplicate the same
/// provider-failure-to-<see cref="VoiceReplyEvent.AudioFailedEvent"/> handling twice.
/// </summary>
internal static class TextToSpeechStreamer
{
    public static async IAsyncEnumerable<VoiceReplyEvent> StreamAsync(
        ITextToSpeechProvider textToSpeechProvider,
        IVoiceProviderHealthRecorder healthRecorder,
        string text,
        VoiceSettingsDto settings,
        Func<int> nextSequence,
        string userId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        IAsyncEnumerator<byte[]> enumerator;
        Exception? startFailure = null;
        try
        {
            enumerator = textToSpeechProvider.StreamSpeechAsync(text, settings, cancellationToken).GetAsyncEnumerator(cancellationToken);
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

                yield return VoiceReplyEvent.AudioChunkEvent(nextSequence(), enumerator.Current);
            }
        }
    }

    public static async Task<VoiceSettingsDto> ResolveVoiceSettingsAsync(
        ITextToSpeechProvider textToSpeechProvider,
        IUserVoicePreferenceRepository voicePreferences,
        string userId,
        string language,
        CancellationToken cancellationToken)
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

    private static async IAsyncEnumerator<byte[]> EmptyAsyncEnumerator()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static string Truncate(string message) => message.Length > 500 ? message[..500] : message;
}
