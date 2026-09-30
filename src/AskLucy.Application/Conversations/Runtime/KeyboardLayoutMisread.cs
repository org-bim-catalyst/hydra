using System.Text;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// A message typed in English while the keyboard was set to the Arabic layout arrives as Arabic letters
/// that mean nothing ("احص ةث" is "how me"). Acting on such a message is a guess, so the turn is
/// answered in words instead: Lucy says what she read and asks the user to confirm or retype.
/// <para>
/// The decode is deterministic (the standard Arabic 101 key map), and a message only counts as a
/// misread when the decoded text contains real English words. Genuine Arabic decodes to letter soup,
/// so it is never caught, and its capabilities are never blocked.
/// </para>
/// </summary>
public static class KeyboardLayoutMisread
{
    /// <summary>Unshifted Arabic 101 layout, by the Latin key that produces each letter.</summary>
    private static readonly Dictionary<char, char> ArabicToLatin = new()
    {
        ['ض'] = 'q',
        ['ص'] = 'w',
        ['ث'] = 'e',
        ['ق'] = 'r',
        ['ف'] = 't',
        ['غ'] = 'y',
        ['ع'] = 'u',
        ['ه'] = 'i',
        ['خ'] = 'o',
        ['ح'] = 'p',
        ['ش'] = 'a',
        ['س'] = 's',
        ['ي'] = 'd',
        ['ب'] = 'f',
        ['ل'] = 'g',
        ['ا'] = 'h',
        ['ت'] = 'j',
        ['ن'] = 'k',
        ['م'] = 'l',
        ['ئ'] = 'z',
        ['ء'] = 'x',
        ['ؤ'] = 'c',
        ['ر'] = 'v',
        ['ى'] = 'n',
        ['ة'] = 'm',
        ['و'] = ',',
        ['ز'] = '.',
        ['ك'] = ';',
        ['ط'] = '\'',
        ['ظ'] = '/',
        ['ج'] = '[',
        ['د'] = ']',
        ['أ'] = 'H',
        ['إ'] = 'H',
    };

    /// <summary>The lam-alef ligature is the B key on this layout; it is one glyph (or two letters).</summary>
    private static readonly char[] LamAlefLigatures = ['ﻻ', 'ﻼ', 'ﻵ', 'ﻶ', 'ﻷ', 'ﻸ', 'ﻹ', 'ﻺ'];

    /// <summary>Everyday English words: a decode needs several of them to be taken as English and not letter soup.</summary>
    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "you", "for", "are", "with", "this", "that", "have", "from", "not", "can", "will", "what", "when", "where", "which",
        "who", "how", "why", "show", "tell", "give", "find", "take", "make", "open", "close", "edit", "move", "zoom", "look", "see", "let",
        "please", "thanks", "thank", "hello", "hey", "help", "map", "site", "size", "area", "outline", "boundary", "mall", "park", "city",
        "me", "my", "we", "us", "it", "is", "in", "on", "of", "to", "do", "go", "no", "so", "up", "or", "if", "at", "be", "he", "an", "as",
        "all", "any", "about", "into", "then", "there", "here", "your", "our", "them", "they", "its", "was", "has", "had", "does",
    };

    /// <summary>
    /// The English the message decodes to, when it looks like English typed on the Arabic layout; null
    /// for ordinary text of any language, including genuine Arabic.
    /// </summary>
    public static string? TryDecode(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var letters = message.Count(char.IsLetter);
        if (letters == 0)
        {
            return null;
        }

        // Mostly Arabic-script letters to begin with: ordinary English is left alone without further work.
        var arabic = message.Count(c => c is >= '؀' and <= 'ۿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿');
        if (arabic < letters * 0.6)
        {
            return null;
        }

        var decoded = Decode(message);
        var words = decoded
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetter).ToArray()))
            .Where(word => word.Length > 0)
            .ToList();
        if (words.Count == 0)
        {
            return null;
        }

        var known = words.Where(word => CommonWords.Contains(word)).ToList();
        var enough = known.Count >= 2 && known.Any(word => word.Length >= 3) && known.Count >= words.Count * 0.4;
        return enough ? decoded.Trim() : null;
    }

    /// <summary>The message with each Arabic-layout letter replaced by the Latin key that typed it; everything else is kept.</summary>
    public static string Decode(string message)
    {
        var builder = new StringBuilder(message.Length);
        for (var i = 0; i < message.Length; i++)
        {
            var c = message[i];
            if (Array.IndexOf(LamAlefLigatures, c) >= 0 || (c == 'ل' && i + 1 < message.Length && message[i + 1] == 'ا'))
            {
                builder.Append('b');
                if (c == 'ل')
                {
                    i++;
                }

                continue;
            }

            builder.Append(ArabicToLatin.TryGetValue(c, out var latin) ? latin : c);
        }

        return builder.ToString();
    }

    /// <summary>The instruction that turns the misread into a clarifying reply instead of a guess.</summary>
    public static string ClarificationInstruction(string decoded) =>
        "The user's last message looks like English typed by mistake while the keyboard was set to the Arabic " +
        $"layout. Decoded with the standard Arabic key map it reads approximately: \"{decoded}\". " +
        "Do NOT act on it and do not call anything. Say, briefly and kindly, what you think they meant " +
        "(quote the decoded text), say it may contain typos, and ask them to confirm or to retype it with the " +
        "keyboard on English. Reply in the language they normally write in if you can tell, otherwise English.";
}
