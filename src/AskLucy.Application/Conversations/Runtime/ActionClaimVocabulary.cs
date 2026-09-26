using System.Text.RegularExpressions;
using AskLucy.Application.Conversations.Capabilities;

namespace AskLucy.Application.Conversations.Runtime;

/// <summary>One capability's share of the claim vocabulary: the words a reply uses to say it ran.</summary>
/// <param name="Key">The capability key an <see cref="ActionAttempt"/> is matched against.</param>
/// <param name="Verbs">Completed-action forms — "highlighted", "shown" — never the base form.</param>
/// <param name="Nouns">What the action acts on — "boundary", "panel" — which names the capability in a sentence.</param>
internal sealed record CapabilityClaimTerms(string Key, IReadOnlySet<string> Verbs, IReadOnlySet<string> Nouns);

/// <summary>
/// The words that count as a claim to have done something, <b>derived</b> from the capability
/// vocabulary rather than written out by hand (specs/068 T029).
///
/// <para>
/// Hand-written patterns are this feature's main residual risk: a capability added later would
/// keep working while its claims quietly stopped being checked, and nothing would fail. Here the
/// source is the capability key constants themselves, so a new capability contributes its verb and
/// its nouns the moment it is added — and <c>ActionClaimVocabularyTests</c> fails the build if a
/// registered capability is missing from <see cref="Catalog"/>.
/// </para>
/// </summary>
internal static partial class ActionClaimVocabulary
{
    /// <summary>
    /// The closed vocabulary, as key/label pairs taken straight off the capability types. Compile-time
    /// references, so renaming a key cannot leave a stale pattern behind.
    /// </summary>
    private static readonly (string Key, string Label)[] Catalog =
    [
        (AdjustViewerFocusCapability.CapabilityKey, "Zoom the viewer"),
        (LoadViewerContentCapability.CapabilityKey, "Load into the viewer"),
        (OpenLivePanelCapability.CapabilityKey, "Open panel"),
        (OpenSolarAnalysisCapability.CapabilityKey, "Show sun & shadows"),
        (PresentPanelContentCapability.CapabilityKey, "Show it as a panel"),
        (RequestSiteAnalysisCapability.CapabilityKey, "Run a full site analysis"),
        (ResolveLocationCapability.CapabilityKey, "Find a place"),
        (ResolveSiteBoundaryCapability.CapabilityKey, "Highlight the site boundary"),
        (SearchKnowledgeBaseCapability.CapabilityKey, "Search my knowledge bases"),
        (SearchMemoryCapability.CapabilityKey, "Check what I remember"),
        (SetSiteBoundaryMembersCapability.CapabilityKey, "Choose the site buildings"),
    ];

    /// <summary>Words that carry no capability meaning and would match anything.</summary>
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "my", "it", "as", "into", "what", "i", "full", "and", "of", "live",
    };

    /// <summary>
    /// Past forms English does not build with "-ed". Small and closed because the source vocabulary
    /// is: every verb here comes from a capability key or label, not from open text.
    /// </summary>
    private static readonly Dictionary<string, string[]> IrregularPastForms = new(StringComparer.Ordinal)
    {
        ["choose"] = ["chose", "chosen"],
        ["find"] = ["found"],
        ["show"] = ["shown", "showed"],
        ["run"] = ["ran"],
    };

    /// <summary>
    /// Completed-action verbs that need no capability noun beside them to count as a claim, because
    /// they have no conversational reading — "I've shown you Al Safa Park 2" is about the workspace
    /// or it is about nothing. This is the exact sentence shape of the reported defect.
    ///
    /// <para>
    /// Deliberately a subset. "found", "searched", "checked", "ran", "resolved" and "requested" are
    /// all generated too, but each has an everyday sense ("I found that the document says…") that
    /// would make a claim-free sentence look like a claim, so they only count alongside a noun that
    /// names the capability. <c>ActionClaimVocabularyTests</c> asserts every verb listed here is one
    /// the catalog actually generates, so this stays a filter on the vocabulary and never becomes a
    /// second, hand-maintained one.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> UnambiguousVerbs = new(StringComparer.Ordinal)
    {
        "shown", "showed", "zoomed", "loaded", "opened", "highlighted",
    };

    internal static IReadOnlyList<CapabilityClaimTerms> Capabilities { get; } = [.. Catalog.Select(Derive)];

    /// <summary>Every completed-action form the catalog produces, across all capabilities.</summary>
    internal static IReadOnlySet<string> AllVerbs { get; } =
        Capabilities.SelectMany(c => c.Verbs).ToHashSet(StringComparer.Ordinal);

    /// <summary>The subset that stands on its own; see <see cref="UnambiguousVerbs"/>.</summary>
    internal static IReadOnlySet<string> StandaloneVerbs { get; } = UnambiguousVerbs;

    /// <summary>
    /// True when <paramref name="sentence"/> states in the first person that an action was carried
    /// out. Future and conditional forms ("I'll show you", "I can open that") are not claims: only
    /// the completed forms are generated, so the base verb never matches.
    /// </summary>
    internal static bool IsActionClaim(string sentence)
    {
        if (!FirstPerson().IsMatch(sentence))
        {
            return false;
        }

        var words = Words(sentence);
        return words.Overlaps(StandaloneVerbs) || Capabilities.Any(c => Names(c, words));
    }

    /// <summary>
    /// Which capabilities the sentence actually names — verb <b>and</b> noun both present. Empty
    /// when a claim is made in general terms, which the gate then checks against the turn as a whole.
    /// </summary>
    internal static IReadOnlyList<string> CapabilitiesNamedIn(string sentence)
    {
        var words = Words(sentence);
        return [.. Capabilities.Where(c => Names(c, words)).Select(c => c.Key)];
    }

    private static bool Names(CapabilityClaimTerms capability, HashSet<string> words) =>
        words.Overlaps(capability.Verbs) && words.Overlaps(capability.Nouns);

    private static HashSet<string> Words(string sentence) =>
        [.. WordBreak().Split(sentence).Where(w => w.Length > 0).Select(Normalize)];

    /// <summary>
    /// One spelling per word: lower case, and singular. Everything in the vocabulary and everything
    /// matched against it goes through here, so "Highlight" from a label and "highlighted" from a
    /// reply — or "knowledge bases" and "knowledge base" — meet as the same token rather than
    /// relying on every comparison site to remember a case-insensitive comparer.
    /// </summary>
    private static string Normalize(string word)
    {
        var lowered = word.ToLowerInvariant();
        return lowered.Length > 3 && lowered.EndsWith('s') && !lowered.EndsWith("ss", StringComparison.Ordinal)
            ? lowered[..^1]
            : lowered;
    }

    private static CapabilityClaimTerms Derive((string Key, string Label) source)
    {
        var keyTokens = source.Key.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var labelTokens = WordBreak().Split(source.Label).Where(w => w.Length > 0).ToArray();

        // The leading token of a key ("resolve_site_boundary") and of a label ("Find a place") is
        // the action; everything after it is what the action acts on.
        var verbs = new[] { keyTokens[0], labelTokens[0] }
            .Select(v => v.ToLowerInvariant())
            .SelectMany(PastForms)
            .ToHashSet(StringComparer.Ordinal);

        var nouns = keyTokens.Skip(1).Concat(labelTokens.Skip(1))
            .Where(t => !Ignored.Contains(t))
            .Select(Normalize)
            .ToHashSet(StringComparer.Ordinal);

        return new CapabilityClaimTerms(source.Key, verbs, nouns);
    }

    private static IEnumerable<string> PastForms(string verb)
    {
        if (IrregularPastForms.TryGetValue(verb, out var irregular))
        {
            return irregular;
        }

        return [verb.EndsWith('e') ? verb + "d" : verb + "ed"];
    }

    /// <summary>A first-person subject — the difference between claiming and describing.</summary>
    [GeneratedRegex(@"\b(?:I|I'?ve|I'?m|we|we'?ve)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstPerson();

    [GeneratedRegex(@"[^\p{L}]+")]
    private static partial Regex WordBreak();
}
