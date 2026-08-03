using System.Text;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Commands.SynthesizeSpeech;

/// <summary>
/// Splits the already-known text into sentence-sized chunks (same boundary rule as
/// <see cref="Commands.StreamVoiceReply.StreamVoiceReplyCommandHandler"/>) purely so playback
/// can begin after the first sentence synthesizes instead of waiting for the whole reply —
/// there's no LLM stream to interleave with here, unlike that handler.
/// </summary>
public sealed partial class SynthesizeSpeechCommandHandler(
    ITextToSpeechProvider textToSpeechProvider,
    IVoiceProviderHealthRecorder healthRecorder,
    IUserVoicePreferenceRepository voicePreferences,
    ICurrentUserAccessor currentUser) : IStreamRequestHandler<SynthesizeSpeechCommand, VoiceReplyEvent>
{
    public async IAsyncEnumerable<VoiceReplyEvent> Handle(
        SynthesizeSpeechCommand request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var settings = await TextToSpeechStreamer.ResolveVoiceSettingsAsync(
            textToSpeechProvider, voicePreferences, userId, request.Language, cancellationToken);

        var sequence = 0;
        foreach (var sentence in SplitIntoSentences(request.Text))
        {
            var failed = false;
            await foreach (var audioEvent in TextToSpeechStreamer.StreamAsync(
                textToSpeechProvider, healthRecorder, sentence, settings, () => sequence++, userId, cancellationToken))
            {
                if (audioEvent.Type == "audio-failed")
                {
                    failed = true;
                }

                yield return audioEvent;
            }

            if (failed)
            {
                yield break;
            }
        }

        yield return VoiceReplyEvent.DoneEvent();
    }

    private static IEnumerable<string> SplitIntoSentences(string text)
    {
        var buffer = new StringBuilder(text);
        while (TryExtractSentence(buffer, out var sentence))
        {
            yield return sentence;
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString().Trim();
        }
    }

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
