using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — deterministic weighted candidate scoring, a direct port
/// of the reference notebook's <c>SOURCE_RELIABILITY</c>/<c>score_candidate</c>. Every factor is
/// named and independently inspectable (<see cref="ScoredBoundaryCandidate.ScoreBreakdown"/>) so
/// a result can explain itself (FR-005) rather than being a black-box score.
/// </summary>
public sealed class BoundaryCandidateScorer(IOptions<BoundaryScoringOptions> options)
{
    /// <summary>Illustrative, ranking-only reliability values (not measured probabilities) — mirrors the notebook's own framing.</summary>
    private static readonly IReadOnlyDictionary<SiteBoundarySource, double> SourceReliability = new Dictionary<SiteBoundarySource, double>
    {
        [SiteBoundarySource.GovernmentCadastral] = 1.00,
        [SiteBoundarySource.UploadedBoundary] = 0.90,
        [SiteBoundarySource.RenderedMapExtraction] = 0.85,
        [SiteBoundarySource.OsmBoundary] = 0.80,
        [SiteBoundarySource.AiInterpretation] = 0.55,
        [SiteBoundarySource.ManualFallback] = 0.30,
    };

    private static readonly string[] LandUseRelevantTagKeys = ["leisure", "landuse", "amenity", "tourism", "shop"];

    /// <summary>
    /// Words that say what kind of site a name is rather than which one, each mapped to that kind.
    /// Tag values go through the same map, so <c>shop=mall</c> counts as "retail" and
    /// <c>leisure=park</c> as "park".
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> SiteKindWords = new Dictionary<string, string>
    {
        ["mall"] = "retail",
        ["shopping"] = "retail",
        ["center"] = "retail",
        ["centre"] = "retail",
        ["plaza"] = "retail",
        ["souk"] = "retail",
        ["market"] = "retail",
        ["marketplace"] = "retail",
        ["park"] = "park",
        ["garden"] = "park",
        ["gardens"] = "park",
        ["school"] = "school",
        ["hospital"] = "hospital",
        ["university"] = "university",
        ["college"] = "college",
    };

    public IReadOnlyList<ScoredBoundaryCandidate> ScoreAll(IReadOnlyList<BoundaryCandidate> candidates, string siteNameQuery)
    {
        return candidates
            .Select(c => Score(c, siteNameQuery))
            .OrderByDescending(c => c.Score)
            .ToList();
    }

    private ScoredBoundaryCandidate Score(BoundaryCandidate candidate, string siteNameQuery)
    {
        var opts = options.Value;
        var breakdown = new Dictionary<string, double>
        {
            ["source_reliability"] = SourceReliability.GetValueOrDefault(candidate.Source, 0.5),
            ["name_match"] = ScoreNameMatch(candidate, siteNameQuery),
            ["geometry_quality"] = ScoreGeometryQuality(candidate.AreaSquareMeters),
            ["center_proximity"] = ScoreCenterProximity(candidate.DistanceToCenterMeters, opts.SearchRadiusMeters),
            ["landuse_agreement"] = LandUseRelevantTagKeys.Any(k => candidate.Tags.ContainsKey(k)) ? 1.0 : 0.3,
        };

        var total =
            (breakdown["source_reliability"] * opts.SourceReliabilityWeight) +
            (breakdown["name_match"] * opts.NameMatchWeight) +
            (breakdown["geometry_quality"] * opts.GeometryQualityWeight) +
            (breakdown["center_proximity"] * opts.CenterProximityWeight) +
            (breakdown["landuse_agreement"] * opts.LandUseAgreementWeight);

        return new ScoredBoundaryCandidate(candidate, Math.Round(total, 3), breakdown);
    }

    /// <summary>
    /// Checks both <see cref="BoundaryCandidate.Name"/> (OSM's own "name" tag — often in the
    /// site's local script/language, e.g. Arabic "حديقة الصفا 2") and a "name:en" tag if present,
    /// so a real match isn't scored as 0 purely because the query happens to be in a different
    /// script than the primary name tag (observed live: this was the second contributing cause
    /// of a production mis-pick, alongside an over-broad tag filter — see
    /// OverpassBoundaryCandidateProvider's CandidateTagFilters doc comment for the full story).
    /// "alt_name" is checked as well: OSM keeps a site's other names there, and a site merged from
    /// several separately-named parts carries the other parts' names in it.
    /// </summary>
    private static double ScoreNameMatch(BoundaryCandidate candidate, string siteNameQuery)
    {
        var query = (siteNameQuery ?? string.Empty).Trim().ToLowerInvariant();
        if (query.Length == 0)
        {
            return 0.0;
        }

        var alternativeNames = (candidate.Tags.GetValueOrDefault("alt_name") ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var candidateNames = new[] { candidate.Name, candidate.Tags.GetValueOrDefault("name:en") }
            .Concat(alternativeNames)
            .Where(n => !string.IsNullOrWhiteSpace(n));

        var tagKinds = LandUseRelevantTagKeys
            .Select(key => candidate.Tags.GetValueOrDefault(key))
            .Where(value => value is not null && SiteKindWords.ContainsKey(value))
            .Select(value => SiteKindWords[value!])
            .ToHashSet(StringComparer.Ordinal);

        return candidateNames.Select(n => ScoreOneName(n!, query, tagKinds)).DefaultIfEmpty(0.0).Max();
    }

    private static double ScoreOneName(string candidateName, string query, IReadOnlySet<string> tagKinds)
    {
        var name = candidateName.Trim().ToLowerInvariant();

        if (query.Contains(name) || name.Contains(query))
        {
            return 1.0;
        }

        if (IsSameSiteSpelledDifferently(WordsOf(name), WordsOf(query), tagKinds))
        {
            return 1.0;
        }

        // "burjuman" is in "Bur Juman Shopping Center" too, once the spaces are gone.
        var compactName = string.Concat(WordsOf(name));
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Any(word => word.Length > 3 && (name.Contains(word) || compactName.Contains(word))))
        {
            return 0.5;
        }

        return 0.0;
    }

    /// <summary>
    /// The same site named with different spacing and a different word for its kind:
    /// "BurJuman Mall" against OSM's "Bur Juman Shopping Center" (shop=mall). The distinctive
    /// part of both names, with spaces, punctuation and kind words removed, must be identical, and
    /// any kind the query names must agree with the candidate's. Found live on 2026-09-25: before
    /// this the mall scored no name match at all, and "Burjuman Park" next door, which merely
    /// contains the word "burjuman", won.
    /// </summary>
    private static bool IsSameSiteSpelledDifferently(List<string> nameWords, List<string> queryWords, IReadOnlySet<string> tagKinds)
    {
        var queryCore = CoreOf(queryWords);
        if (queryCore.Length < 4 || queryCore != CoreOf(nameWords))
        {
            return false;
        }

        var queryKinds = KindsOf(queryWords);
        return queryKinds.Count == 0 || queryKinds.Overlaps(KindsOf(nameWords).Concat(tagKinds));
    }

    private static string CoreOf(IEnumerable<string> words) =>
        string.Concat(words.Where(w => w != "the" && !SiteKindWords.ContainsKey(w)));

    private static HashSet<string> KindsOf(IEnumerable<string> words) =>
        words.Where(SiteKindWords.ContainsKey).Select(w => SiteKindWords[w]).ToHashSet(StringComparer.Ordinal);

    private static List<string> WordsOf(string text)
    {
        var words = new List<string>();
        var word = new System.Text.StringBuilder();
        foreach (var ch in text.Append(' '))
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

    /// <summary>
    /// FR-013 — penalizes implausibly small (&lt;50 m², likely a mapping error) or implausibly
    /// large (&gt;500,000 m², likely an administrative boundary rather than a specific site)
    /// polygons, matching the notebook's thresholds exactly.
    /// </summary>
    private static double ScoreGeometryQuality(double areaSquareMeters) => areaSquareMeters switch
    {
        < 50 => 0.2,
        > 500_000 => 0.4,
        _ => 1.0,
    };

    private static double ScoreCenterProximity(double distanceToCenterMeters, int searchRadiusMeters) =>
        Math.Max(0.0, 1.0 - (distanceToCenterMeters / searchRadiusMeters));
}
