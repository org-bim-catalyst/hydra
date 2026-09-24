using System.Text;
using System.Text.RegularExpressions;

namespace AskLucy.Application.Ai;

/// <summary>
/// Cuts a growing stream of text into sentence-sized pieces as soon as each one is complete.
///
/// <para>
/// Extracted from <c>StreamVoiceReplyCommandHandler</c>, which has segmented replies this way
/// since specs/029. specs/068 needs the <b>same</b> granularity for the claim gate rather than a
/// second, subtly different one: voice consumes the gated text stream, so a gate that cut
/// sentences differently from the speaker would hold or release audio at boundaries the speaker
/// never chose, and reintroduce the text/audio desync those specs closed
/// (contracts/turn-outcome.md §3, "Ordering with voice output").
/// </para>
/// </summary>
internal static partial class SentenceSegmenter
{
    /// <summary>
    /// How much text may accumulate with no boundary in it before it is cut anyway. Beyond this
    /// the wait is worse than an imperfect break: for voice nothing is heard at all until the run
    /// finally ends, and for the claim gate nothing is shown.
    /// </summary>
    internal const int MaxUnbrokenSentenceLength = 160;

    /// <summary>
    /// Takes the next complete segment <b>verbatim</b>, keeping its punctuation and its trailing
    /// whitespace. The claim gate needs this form: a released sentence has to be byte-identical to
    /// what the model produced, so it cannot use a trimming overload that silently drops the space
    /// between two sentences.
    /// </summary>
    internal static bool TryTakeSegment(StringBuilder buffer, out string segment)
    {
        var text = buffer.ToString();
        var match = SentenceBoundary().Match(text);
        if (match.Success)
        {
            var endIndex = match.Index + match.Length;
            segment = text[..endIndex];
            buffer.Remove(0, endIndex);
            return true;
        }

        // No boundary yet. A run this long is not one sentence — it is a list item, a heading or
        // a clause the model has not finished punctuating, and waiting for it is what made Lucy
        // start speaking long after the text appeared. Break at the last word boundary instead.
        if (text.Length >= MaxUnbrokenSentenceLength)
        {
            var breakAt = text.LastIndexOf(' ', MaxUnbrokenSentenceLength - 1);
            if (breakAt <= 0)
            {
                breakAt = MaxUnbrokenSentenceLength;
            }

            segment = text[..breakAt];
            buffer.Remove(0, breakAt);
            return true;
        }

        segment = string.Empty;
        return false;
    }

    /// <summary>
    /// Takes the next segment trimmed, skipping any that is whitespace only — the form the voice
    /// path wants, where a blank line is nothing to synthesise.
    /// </summary>
    internal static bool TryExtractSentence(StringBuilder buffer, out string sentence)
    {
        while (TryTakeSegment(buffer, out var segment))
        {
            sentence = segment.Trim();
            if (sentence.Length > 0)
            {
                return true;
            }
        }

        sentence = string.Empty;
        return false;
    }

    /// <summary>
    /// Terminal punctuation, or a line break.
    /// <para>
    /// The line break matters as much as the punctuation. Replies are markdown — headings and
    /// bullet lists whose items routinely carry no full stop at all — so on `[.!?]` alone the
    /// buffer ran through many lines before a single sentence could be synthesised, and the
    /// first audio arrived long after the text had finished rendering. A line is a natural unit
    /// of speech anyway.
    /// </para>
    /// </summary>
    [GeneratedRegex(@"[.!?]+(\s|$)|\r?\n+")]
    private static partial Regex SentenceBoundary();
}
