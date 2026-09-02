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
        [SiteBoundarySource.OsmBoundary] = 0.80,
        [SiteBoundarySource.AiInterpretation] = 0.55,
        [SiteBoundarySource.ManualFallback] = 0.30,
    };

    private static readonly string[] LandUseRelevantTagKeys = ["leisure", "landuse", "amenity", "tourism"];

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
    /// </summary>
    private static double ScoreNameMatch(BoundaryCandidate candidate, string siteNameQuery)
    {
        var query = (siteNameQuery ?? string.Empty).Trim().ToLowerInvariant();
        if (query.Length == 0)
        {
            return 0.0;
        }

        var candidateNames = new[] { candidate.Name, candidate.Tags.GetValueOrDefault("name:en") }
            .Where(n => !string.IsNullOrWhiteSpace(n));

        return candidateNames.Select(n => ScoreOneName(n!, query)).DefaultIfEmpty(0.0).Max();
    }

    private static double ScoreOneName(string candidateName, string query)
    {
        var name = candidateName.Trim().ToLowerInvariant();

        if (query.Contains(name) || name.Contains(query))
        {
            return 1.0;
        }

        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Any(word => word.Length > 3 && name.Contains(word)))
        {
            return 0.5;
        }

        return 0.0;
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
