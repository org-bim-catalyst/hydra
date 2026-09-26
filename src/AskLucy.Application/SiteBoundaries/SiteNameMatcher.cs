using System.Text;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/077 — decides whether a building's name carries a site's name: "Burjuman Office Tower"
/// and "BurJuman Arjaan by Rotana" carry "BurJuman Mall"'s; "City Seasons Towers Hotel", next door
/// but separately owned, does not. Pure text, so it is unit-testable and the same rule serves the
/// search after resolution and the user's own "include the office tower" later.
/// </summary>
public static class SiteNameMatcher
{
    /// <summary>Words that describe a kind of site rather than name one.</summary>
    private static readonly HashSet<string> GenericWords = ["the", "of", "mall", "shopping", "center", "centre"];

    /// <summary>
    /// Place names too common to identify a site on their own. The Dubai Mall's core would be
    /// "dubai" once "the" and "mall" are stripped, and every "Dubai …" building in range would
    /// then count as part of it; for such a site the whole name ("dubaimall") is the core instead.
    /// </summary>
    private static readonly HashSet<string> PlaceNames =
    [
        "dubai", "abudhabi", "sharjah", "ajman", "fujairah", "emirates", "uae",
        "muscat", "oman", "doha", "qatar", "riyadh", "jeddah", "saudi", "bahrain", "manama", "kuwait",
        "city", "downtown", "marina",
    ];

    /// <summary>Cores shorter than this are too weak to tie two buildings together.</summary>
    private const int MinimumCoreLength = 4;

    /// <summary>A core this long tolerates one mistyped letter: OSM spells the tower across from BurJuman "Burjman".</summary>
    private const int MinimumFuzzyCoreLength = 6;

    /// <summary>The distinctive part of each name, lower-cased and joined: "Bur Juman Shopping Center" → "burjuman".</summary>
    public static IReadOnlySet<string> CoresOf(IEnumerable<string?> names)
    {
        var cores = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var words = WordsOf(name);
            var core = string.Concat(words.Where(w => !GenericWords.Contains(w)));
            if (PlaceNames.Contains(core))
            {
                core = string.Concat(words.Where(w => w != "the"));
            }

            if (core.Length >= MinimumCoreLength)
            {
                cores.Add(core);
            }
        }

        return cores;
    }

    /// <summary>
    /// True when any of <paramref name="names"/> contains one of <paramref name="cores"/> —
    /// spaced or not ("Bur Juman" counts), and for a long core with at most one letter wrong.
    /// </summary>
    public static bool Matches(IEnumerable<string?> names, IReadOnlySet<string> cores)
    {
        foreach (var name in names)
        {
            var words = WordsOf(name);
            if (words.Count == 0)
            {
                continue;
            }

            var joined = string.Concat(words);
            foreach (var core in cores)
            {
                if (joined.Contains(core, StringComparison.Ordinal))
                {
                    return true;
                }

                if (core.Length >= MinimumFuzzyCoreLength && SpansOf(words).Any(span => IsWithinOneEdit(span, core)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Each word, and each run of up to three adjacent words joined — the pieces a spaced or split spelling of a core can occupy.</summary>
    private static IEnumerable<string> SpansOf(List<string> words)
    {
        for (var start = 0; start < words.Count; start++)
        {
            var span = new StringBuilder();
            for (var end = start; end < Math.Min(words.Count, start + 3); end++)
            {
                span.Append(words[end]);
                yield return span.ToString();
            }
        }
    }

    private static bool IsWithinOneEdit(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1)
        {
            return false;
        }

        var i = 0;
        var j = 0;
        var edits = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                i++;
                j++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            if (a.Length > b.Length)
            {
                i++;
            }
            else if (b.Length > a.Length)
            {
                j++;
            }
            else
            {
                i++;
                j++;
            }
        }

        return edits + (a.Length - i) + (b.Length - j) <= 1;
    }

    private static List<string> WordsOf(string? name)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            return words;
        }

        var word = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant().Append(' '))
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
            }
            else if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }

        return words;
    }
}
