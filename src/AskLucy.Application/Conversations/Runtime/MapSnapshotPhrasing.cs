using System.Text.RegularExpressions;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>
/// specs/083-map-snapshot — recognises a request that could have meant "give me a picture of it"
/// or "put it on the map", and reads the place it was about.
///
/// <para>
/// <b>Why this is deterministic rather than a model judgement.</b> The two answers are opposites:
/// one returns an image, the other moves the viewer's camera. A model that guesses wrong has
/// already moved the user's map by the time anyone could ask, and the user asked for a picture.
/// A closed set of phrasings matched here is measurable in a unit test and cannot regress when
/// a provider changes its model behind the same name.
/// </para>
///
/// <para>
/// <b>Deliberately narrow.</b> "Show me Al Safa Park 2" is the long-standing way to ask for a
/// place to be put on the map, and it is not ambiguous: it is what every "show me &lt;place&gt;"
/// turn has meant for years, and it stays exactly that. Only phrasings that ask what a place
/// <i>looks like</i> — which have no navigational reading at all — count, and only when they
/// name a place: the card's second row moves the map <i>there</i>, so a place it cannot name is
/// a place it cannot offer.
/// </para>
///
/// <para>
/// A message that names the picture outright ("a screenshot of…", "an aerial view of…") is not
/// ambiguous either: the deciding model routes that on the capability's own "when to use" text,
/// and a card would only get in the way.
/// </para>
/// </summary>
public static partial class MapSnapshotPhrasing
{
    /// <summary>
    /// The place an ambiguous request named, or null when the request is not one of the ambiguous
    /// phrasings — or is one, but names no place the card could offer to move the map to.
    /// </summary>
    public static string? TryDetectAmbiguousPlace(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var normalized = Normalize(message);

        // The picture is named outright, so the deciding model has everything it needs.
        if (PictureWord().IsMatch(normalized))
        {
            return null;
        }

        // "How does it look" is the weather's phrasing too, and the weather is never a picture
        // of a site.
        if (WeatherWord().IsMatch(normalized))
        {
            return null;
        }

        foreach (var pattern in PlacePatterns)
        {
            var match = pattern.Match(message);
            if (match.Success)
            {
                return CleanPlace(match.Groups["place"].Value);
            }
        }

        return null;
    }

    /// <summary>Lower case, no punctuation, single-spaced — so "How does it look?" and "how does it look" are one phrasing.</summary>
    private static string Normalize(string message) =>
        Whitespace().Replace(Punctuation().Replace(message.ToLowerInvariant(), " "), " ").Trim();

    /// <summary>
    /// The place's own name, with a back-reference to the place already on screen left unnamed
    /// and anything that is plainly not a place rejected.
    /// </summary>
    private static string? CleanPlace(string raw)
    {
        var place = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim('?', '!', '.', ',', ';', ':')
            .Trim();

        if (place.Length == 0 || place.Length > 80)
        {
            return null;
        }

        var words = place.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "this site", "the current location" — the place is the one already on screen, which the
        // chat knows and the message does not name. A card offering to move the map there would
        // offer a move the map has already made.
        if (GenericPlaces.Contains(place.ToLowerInvariant()))
        {
            return null;
        }

        // The article belongs to the phrase, not to the name: "the new development" is a place
        // called "new development". Checked against the generic set again afterwards, because
        // "the site" and "the park" are back-references once their article is gone.
        if (words.Length > 1 && Articles.Contains(words[0].ToLowerInvariant()))
        {
            place = string.Join(' ', words[1..]);
            words = words[1..];
        }

        if (GenericPlaces.Contains(place.ToLowerInvariant()))
        {
            return null;
        }

        // "how does that document look" is about a document. Offering it a map move would be
        // nonsense, so a phrase that is plainly not a place is not offered one.
        if (words.Any(w => NotAPlace.Contains(w.ToLowerInvariant())))
        {
            return null;
        }

        // A trailing preposition means the phrase carried on past the place ("the place to look
        // at"), and what followed was not the place's name.
        if (TrailingStopWords.Contains(words[^1].ToLowerInvariant()))
        {
            return null;
        }

        return place;
    }

    /// <summary>Back-references to the place already on screen, and pronouns — named, but not by name.</summary>
    private static readonly HashSet<string> GenericPlaces = new(StringComparer.Ordinal)
    {
        "i", "we", "you", "he", "she", "it", "they", "me", "us", "him", "her", "them",
        "this", "that", "these", "those", "here", "there", "the", "a", "an",
        "site", "the site", "this site", "current site", "the current site",
        "place", "the place", "this place",
        "area", "the area", "this area",
        "location", "the location", "this location", "current location", "the current location",
        "park", "the park", "this park",
        "plot", "the plot", "this plot",
        "building", "the building", "this building",
        "everything", "it all",
    };

    /// <summary>
    /// Words that mean the thing being asked about is not a place, so no map offer can be made
    /// for it. Deliberately a closed list of everyday nouns rather than a "looks like a place"
    /// heuristic — a wrong guess here offers the user a map move for a document.
    /// </summary>
    private static readonly HashSet<string> NotAPlace = new(StringComparer.Ordinal)
    {
        "document", "doc", "file", "pdf", "text", "code", "plan", "drawing", "report",
        "email", "message", "answer", "question", "word", "sentence", "paragraph",
        "list", "table", "chart", "data", "graph", "layout", "idea", "concept",
        "number", "name", "weather", "sky", "room", "screen", "function", "app",
        "application", "interface", "logo", "icon", "video", "folder", "note", "notes",
    };

    /// <summary>Articles, which belong to the phrase rather than to the name.</summary>
    private static readonly HashSet<string> Articles = new(StringComparer.Ordinal) { "the", "a", "an" };

    /// <summary>Words that can only be the tail of a phrase the place was not the whole of.</summary>
    private static readonly HashSet<string> TrailingStopWords = new(StringComparer.Ordinal)
    {
        "to", "for", "at", "in", "on", "from", "with", "and", "or", "of", "about", "around", "near",
    };

    /// <summary>The two shapes that carry a place name, in the order they are tried.</summary>
    private static readonly Regex[] PlacePatterns =
    [
        // "how does Al Safa Park 2 look", "what does Dubai Mall look like"
        QuestionPlace(),
        // "show me how Al Safa Park 2 looks"
        ShowLookPlace(),
    ];

    /// <summary>
    /// The two shapes, both anchored at the end so that a trailing qualifier ("…look from above")
    /// rules the phrase out: "from above" is a picture request the deciding model already handles.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:please\s+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:how\s+(?:does|do|did|would|will)|what\s+does)\s+(?<place>.+?)\s+looks?(?:\s+like)?(?:\s+(?:please|now|then))?\s*[.!?]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex QuestionPlace();

    [GeneratedRegex(@"^\s*(?:please\s+)?(?:(?:can|could|would|will)\s+you\s+)?(?:please\s+)?(?:show|tell|give)\s+me\s+(?:what\s+|how\s+|the\s+|a\s+|an\s+)?(?<place>.+?)\s+looks?(?:\s+like)?(?:\s+(?:please|now|then))?\s*[.!?]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ShowLookPlace();

    /// <summary>The words that mean a picture was asked for outright.</summary>
    [GeneratedRegex(@"\b(?:screenshot|screen\s+shot|snapshot|photo|photos|photograph|picture|pictures|image|images|capture|captures|aerial|satellite|thumbnail)\b")]
    private static partial Regex PictureWord();

    /// <summary>The words that mean the weather was asked about, which is never a picture of a site.</summary>
    [GeneratedRegex(@"\b(?:weather|rain|raining|rainy|snow|snowing|cloudy|sunny|wind|winds|windy|temperature|forecast|humidity|storm|thunder)\b")]
    private static partial Regex WeatherWord();

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
