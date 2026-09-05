using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.SiteBoundaries;

internal static partial class BoundaryResolutionServiceLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Boundary resolution for chat {UserChatId}: {OutcomeType} (site: {SiteName})")]
    public static partial void Resolved(ILogger logger, Guid userChatId, BoundaryResolutionOutcomeType outcomeType, string siteName);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Boundary candidate provider unavailable for chat {UserChatId}")]
    public static partial void ProviderUnavailable(ILogger logger, Guid userChatId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Boundary resolution for chat {UserChatId}: Gemini reported an observed boundary but it failed the plausibility check, so the mapped geometry was kept unmoved")]
    public static partial void VisionCorrectionRejected(ILogger logger, Guid userChatId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AI vision verification for chat {UserChatId} failed; the deterministically-scored boundary was kept")]
    public static partial void VisionUnavailable(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>
/// specs/042-site-boundary-resolution — orchestrates: search candidates around an
/// already-confirmed location, score them deterministically, classify confidence, and return a
/// typed outcome. Mirrors <c>LocationResolutionService</c>'s discipline: never throws into the
/// calling chat turn (constitution §VIII); every failure path is an explicit, user-facing
/// outcome.
/// </summary>
/// <remarks>
/// The AI vision cross-check (<see cref="IBoundaryVisionAnalyzer"/>) sees two kinds of imagery for
/// the winning candidate: an overhead rendered-map frame from <see cref="ISatelliteImageProvider"/>
/// (Google's roadmap layer, not satellite photography — a real-world fence trace against satellite
/// or street-level imagery was tried and measurably failed four times, see
/// <c>docs/LOCATION_TO_BOUNDARY_END_TO_END.md</c> §9.6), and up to a handful of ground-level photos
/// from <see cref="IStreetViewImageProvider"/>, sampled around the candidate's own mapped perimeter
/// and aimed back at it, kept only as secondary context for telling one shaded shape on the map
/// from another. See §9.7 for the roadmap-tracing approach itself.
/// </remarks>
public sealed class BoundaryResolutionService(
    IBoundaryCandidateProvider candidateProvider,
    BoundaryCandidateScorer scorer,
    ISatelliteImageProvider satelliteImageProvider,
    IStreetViewImageProvider streetViewImageProvider,
    IBoundaryVisionAnalyzer visionAnalyzer,
    IOptions<BoundaryScoringOptions> options,
    ILogger<BoundaryResolutionService> logger) : IBoundaryResolutionService
{
    /// <summary>Approximate radius (meters) for the manual-fallback circle when no real candidate is found — a reasonable "you are roughly here" area, not a claim of precision.</summary>
    private const double ManualFallbackRadiusMeters = 100.0;

    /// <summary>FR-008 — a second-place candidate within this fraction of the winner's score is disclosed as a similarly-plausible alternative rather than silently dropped.</summary>
    private const double AlternativeCandidateScoreMargin = 0.05;

    public async Task<BoundaryResolutionOutcome> ResolveAsync(
        ConfirmedLocationData confirmedLocation, Guid userChatId, CancellationToken cancellationToken = default)
    {
        var center = new GeoPoint(confirmedLocation.Latitude, confirmedLocation.Longitude);
        var opts = options.Value;

        IReadOnlyList<BoundaryCandidate> candidates;
        try
        {
            candidates = await candidateProvider.SearchAsync(center, opts.SearchRadiusMeters, cancellationToken);
        }
        catch (BoundaryProviderUnavailableException ex)
        {
            BoundaryResolutionServiceLog.ProviderUnavailable(logger, userChatId, ex);
            return new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, BoundaryConfirmationTemplates.Unavailable);
        }

        if (candidates.Count == 0)
        {
            var fallback = BuildManualFallback(confirmedLocation.LocationName, center);
            BoundaryResolutionServiceLog.Resolved(logger, userChatId, BoundaryResolutionOutcomeType.NoCandidates, confirmedLocation.LocationName);
            return new BoundaryResolutionOutcome(
                BoundaryResolutionOutcomeType.NoCandidates, fallback,
                BoundaryConfirmationTemplates.NoCandidates(confirmedLocation.LocationName));
        }

        var ranked = scorer.ScoreAll(candidates, confirmedLocation.LocationName);
        var visionAnalysis = await AnalyzeWithVisionAsync(center, ranked, confirmedLocation.LocationName, opts, userChatId, cancellationToken);
        var selection = SelectFinalCandidate(ranked, visionAnalysis, opts);
        var winner = selection.Winner;
        var confidenceLevel = ClassifyConfidence(selection.CombinedScore, opts);

        var topScore = ranked[0].Score;
        var alternativeNames = ranked
            .Where(c => !ReferenceEquals(c, winner)
                && topScore - c.Score <= AlternativeCandidateScoreMargin
                && !string.IsNullOrWhiteSpace(c.Candidate.Name))
            .Select(c => c.Candidate.Name)
            .Distinct()
            .ToList();

        // Gemini traces the shape Google's own roadmap tiles already drew for the named site —
        // a rendered vector polygon, not a real-world property line inferred from ambiguous
        // photography. That distinction is why this now trusts the traced shape outright rather
        // than only ever translating OSM's ring by a centroid offset: translating repeatedly
        // failed to fix a real offset (16/33/21/35 m, always the same direction) because the
        // signal it was translating by came from photography that does not show the boundary
        // clearly enough to correct against. A traced vector polygon is a different, much
        // lower-noise signal — see §9.7. TryBuildVisionTracedGeometry still gates on plausibility
        // (area ratio, distance from center) before trusting it over the mapped OSM ring, and the
        // mapped ring is still the fallback whenever that gate fails.
        //
        // Basemap alignment, which used to run ahead of this, is gone for good: it snapped the
        // ring's centroid onto the geocoded point, and for a park Google returns an establishment
        // POI (location_type ROOFTOP, bounds null) — a marker inside the polygon, not its centre.
        // That dragged a correct ring 24 m north-west.
        var mappedSourceDetail = DescribeSource(winner.Candidate);
        var visionGeometry = TryBuildVisionTracedGeometry(winner, visionAnalysis, center, opts, mappedSourceDetail);
        if (visionAnalysis.ObservedBoundary is not null && visionGeometry is null)
        {
            BoundaryResolutionServiceLog.VisionCorrectionRejected(logger, userChatId);
        }

        var sourceDetail = visionGeometry?.SourceDetail ?? mappedSourceDetail;
        var finalPolygon = visionGeometry?.Polygon ?? winner.Candidate.Polygon.ExteriorRing;
        var finalArea = visionGeometry?.AreaSquareMeters ?? winner.Candidate.AreaSquareMeters;
        var finalSource = visionGeometry?.Source ?? winner.Candidate.Source;

        var confirmedBoundary = new ConfirmedSiteBoundaryData(
            confirmedLocation.LocationName,
            center.Latitude, center.Longitude,
            finalPolygon, finalArea,
            selection.CombinedScore, confidenceLevel,
            finalSource, sourceDetail,
            alternativeNames);

        // Said out loud when it did not happen. The cross-check failing is invisible otherwise:
        // the outline still renders, still says "high confidence", and simply is not corrected —
        // which on 2026-08-31 meant every boundary went uncorrected for hours behind a run of
        // Gemini 503s with nothing on screen to suggest anything was missing (constitution: no
        // silent failures).
        //
        // Checked against trace adoption first, selection.Agreement second: the shipped Gemini
        // prompt no longer shows the model any OSM candidates to pick from (§9.7 update), so
        // SelectedCandidateId — and therefore selection.Agreement — is always "ai_not_used" in
        // production now. Gating this note on Agreement alone would silently stop reporting a
        // successful cross-check the moment the trace itself is what actually got adopted, which
        // is exactly the scenario this whole feature exists to confirm.
        var aiNote = !visionAnalysis.AiUsed
            ? "Note: I couldn't cross-check this against Google's map rendering just now, so the outline is straight from map data and may sit slightly off the fence."
            : visionGeometry is not null
                ? "This was cross-checked against Google's own map rendering by Gemini vision analysis, which traced the site's boundary directly."
                : DescribeAiVerification(selection.Agreement)
                    ?? (visionAnalysis.ObservedBoundary is not null
                        ? "Note: Gemini traced a boundary from Google's map rendering, but it didn't look like a plausible match for this site, so I kept the map-data outline instead."
                        : null);
        var confirmationText = BoundaryConfirmationTemplates.WithAiVerificationNote(
            BoundaryConfirmationTemplates.WithAlternatives(
                BoundaryConfirmationTemplates.Confirmed(confirmedLocation.LocationName, confidenceLevel, sourceDetail),
                alternativeNames),
            aiNote);

        BoundaryResolutionServiceLog.Resolved(logger, userChatId, BoundaryResolutionOutcomeType.Confirmed, confirmedLocation.LocationName);
        return new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, confirmedBoundary, confirmationText);
    }

    /// <summary>
    /// Optional-enhancement step (research.md's Gemini vision cross-check): fetches satellite
    /// imagery and asks <see cref="IBoundaryVisionAnalyzer"/> to pick among the already-ranked
    /// candidates. Never throws and never blocks resolution on failure — an unavailable image or
    /// analyzer failure degrades to <see cref="BoundaryVisionAnalysis.NotConfigured"/>, exactly
    /// like the reference notebook's own graceful fallback.
    /// </summary>
    private async Task<BoundaryVisionAnalysis> AnalyzeWithVisionAsync(
        GeoPoint center, IReadOnlyList<ScoredBoundaryCandidate> ranked, string siteName,
        BoundaryScoringOptions opts, Guid userChatId, CancellationToken cancellationToken)
    {
        if (!opts.EnableAiVisionVerification)
        {
            return BoundaryVisionAnalysis.NotConfigured(
                "AI vision analysis is disabled (BoundaryScoring:EnableAiVisionVerification = false).");
        }

        // specs/044-location-viewer-regression FR-004 / contract B-3: this method's own summary has
        // always claimed it "never throws and never blocks resolution on failure" — but until now
        // nothing enforced that. Both collaborators document a never-throws contract of their own,
        // and both have real escape hatches (EsriSatelliteImageProvider catches only
        // HttpRequestException/TaskCanceledException; GeminiBoundaryVisionAnalyzer rethrows caller
        // cancellation). An escaped exception here discarded a perfectly good deterministic OSM
        // boundary — vision must be able to improve a result, never to remove one.
        try
        {
            // Framed on the best-ranked candidate rather than on the search radius. At the search
            // radius a 90 x 166 m park sat inside a 1 km frame, a few pixels across, with its
            // rendered polygon and label barely legible — the analyzer was being asked to trace
            // something the image did not show clearly. ImageryRadiusFor gives it the site,
            // filling the frame.
            var imageryRadius = ImageryRadiusFor(ranked[0].Candidate, opts);
            var image = await satelliteImageProvider.FetchAsync(center, imageryRadius, cancellationToken);
            if (image is null)
            {
                return BoundaryVisionAnalysis.NotConfigured("No map image available to analyze this run.");
            }

            // Secondary context only, for telling one shaded shape on the map from another when
            // the map alone is ambiguous about which one is the named site. Sampled around the
            // winning candidate's own mapped ring, not the search radius: viewpoints belong on the
            // site being verified.
            var viewpoints = PerimeterViewpointsFor(ranked[0].Candidate.Polygon.ExteriorRing);
            var lookAt = viewpoints.Count > 0 ? GeometryMath.Centroid(ranked[0].Candidate.Polygon.ExteriorRing) : center;
            var streetViews = await streetViewImageProvider.FetchAsync(viewpoints, lookAt, cancellationToken)
                ?? []; // Defensive only: the real provider never returns null, but nothing here should assume every implementation keeps that promise.

            return await visionAnalyzer.AnalyzeAsync(image, streetViews, ranked, siteName, center, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Contract B-2: cancellation is the one exception this service may surface. Callers
            // distinguish caller-cancellation from a budget expiry; swallowing it here would strip
            // the information they need to tell those apart.
            throw;
        }
        catch (Exception ex)
        {
            BoundaryResolutionServiceLog.VisionUnavailable(logger, userChatId, ex);
            return BoundaryVisionAnalysis.NotConfigured($"AI vision analysis failed: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// A direct port of the reference notebook's <c>select_final_candidate</c>: combines the AI's
    /// selection with the deterministic score into one final decision rather than trusting either
    /// signal alone.
    /// - AI agrees with the top-ranked candidate → confidence gets a small agreement boost.
    /// - AI picks a DIFFERENT candidate → the AI's pick wins (visual/name-matching evidence the
    ///   deterministic scorer doesn't have), flagged as an override rather than silently swapped in.
    /// - AI wasn't run, found no match, or named an unknown candidate id → falls back to the
    ///   deterministic top-ranked candidate alone, exactly as before this feature existed.
    /// </summary>
    private static FinalSelection SelectFinalCandidate(
        IReadOnlyList<ScoredBoundaryCandidate> ranked, BoundaryVisionAnalysis vision, BoundaryScoringOptions opts)
    {
        var topRanked = ranked[0];

        if (!vision.AiUsed || vision.SelectedCandidateId is null)
        {
            return new FinalSelection(topRanked, topRanked.Score, "ai_not_used");
        }

        var aiCandidate = ranked.FirstOrDefault(c => c.Candidate.Id == vision.SelectedCandidateId);
        if (aiCandidate is null)
        {
            return new FinalSelection(topRanked, topRanked.Score, "ai_selection_not_found");
        }

        if (aiCandidate.Candidate.Id == topRanked.Candidate.Id)
        {
            var aiConfidence = vision.Confidence ?? 0.0;
            var blended = (Math.Max(topRanked.Score, aiConfidence) * 0.7) + (Math.Min(topRanked.Score, aiConfidence) * 0.3);
            var combined = Math.Min(1.0, blended + 0.05);
            return new FinalSelection(topRanked, Math.Round(combined, 3), "agree");
        }

        var overrideScore = vision.Confidence ?? aiCandidate.Score;
        return new FinalSelection(aiCandidate, Math.Round(overrideScore, 3), "ai_override");
    }

    /// <summary>
    /// Smallest frame the analyzer is asked for, regardless of how small <paramref name="candidate"/>'s
    /// own OSM ring measures. That ring is the same signal the vision cross-check exists to correct
    /// when it is wrong — if it also undershoots the real site's extent, a floor sized only to "a
    /// small site fills the frame" would hand Gemini an image already clipped to OSM's version of
    /// the boundary, unable to show the very extent OSM got wrong. 150 m keeps the frame at Static
    /// Maps' zoom 18 (~346 m across) for any site whose true extent is under roughly 250 m, well
    /// past what a plausible OSM undershoot looks like in practice.
    /// </summary>
    private const double MinimumImageryRadiusMeters = 150;

    /// <summary>Margin around the candidate, so its corners are never on the frame's edge.</summary>
    private const double ImageryFramingMargin = 1.35;

    /// <summary>
    /// How much ground the vision cross-check's image should cover: the candidate's own extent
    /// plus a margin, never more than the search radius that produced it.
    /// </summary>
    private static int ImageryRadiusFor(BoundaryCandidate candidate, BoundaryScoringOptions opts)
    {
        var ring = candidate.Polygon.ExteriorRing;
        if (ring.Count < 3)
        {
            return opts.SearchRadiusMeters;
        }

        var (minLat, minLon, maxLat, maxLon) = GeometryMath.BoundingBox(ring);
        var halfExtent = GeometryMath.DistanceMeters(new GeoPoint(minLat, minLon), new GeoPoint(maxLat, maxLon)) / 2;

        return (int)Math.Clamp(halfExtent * ImageryFramingMargin, MinimumImageryRadiusMeters, opts.SearchRadiusMeters);
    }

    /// <summary>Ground-level viewpoints requested per boundary. Each costs two Google calls (metadata, then the image); kept small enough to bound both latency and Gemini's payload size.</summary>
    private const int MaxStreetViewViewpoints = 4;

    /// <summary>
    /// The midpoint of each edge of <paramref name="ring"/>, evenly downsampled to at most
    /// <see cref="MaxStreetViewViewpoints"/> if the ring has more edges than that.
    /// </summary>
    /// <remarks>
    /// Edges are walked with wraparound (<c>(i + 1) % ring.Count</c>) rather than assuming the
    /// ring is explicitly closed — <see cref="SiteBoundaryPolygon.ExteriorRing"/>'s own contract
    /// says it may or may not repeat its first point as its last, and a candidate source that
    /// omits the closing point must not silently lose its final edge.
    /// </remarks>
    private static IReadOnlyList<GeoPoint> PerimeterViewpointsFor(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3)
        {
            return [];
        }

        var midpoints = new List<GeoPoint>(ring.Count);
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];

            // A ring that repeats its first point as its last produces exactly one zero-length
            // "edge" here (the last point back to the first, both the same point). Its "midpoint"
            // would just be that vertex again — not a real edge to view from, and for a small
            // polygon (a triangle, say) it would occupy one of only a handful of viewpoint slots
            // for nothing. Skipped, not merely tolerated.
            if (a.Latitude == b.Latitude && a.Longitude == b.Longitude)
            {
                continue;
            }

            midpoints.Add(new GeoPoint((a.Latitude + b.Latitude) / 2, (a.Longitude + b.Longitude) / 2));
        }

        if (midpoints.Count <= MaxStreetViewViewpoints)
        {
            return midpoints;
        }

        var stride = (double)midpoints.Count / MaxStreetViewViewpoints;
        var sampled = new List<GeoPoint>(MaxStreetViewViewpoints);
        for (var i = 0; i < MaxStreetViewViewpoints; i++)
        {
            sampled.Add(midpoints[(int)(i * stride)]);
        }

        return sampled;
    }

    /// <summary>Plausibility gate for <see cref="BoundaryVisionAnalysis.ObservedBoundary"/> — the area and position must be broadly consistent with the mapped candidate it is meant to be describing, or it is discarded as an unreliable read rather than trusted at face value.</summary>
    private const double VisionCorrectionMinAreaRatio = 0.3;

    private const double VisionCorrectionMaxAreaRatio = 3.0;

    /// <summary>
    /// Replaces the mapped geometry outright with Gemini's traced shape, once it clears two
    /// plausibility gates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to only translate: extract the offset between the mapped and observed centroids
    /// and carry every mapped vertex along by that offset, never touching the shape itself. That
    /// was deliberate — the observed outline used to come from a model reading real-world
    /// photography (satellite, cross-checked against Street View), an approximate and sometimes
    /// wrong read of an ambiguous scene, so only its position was trusted, never its shape. It was
    /// also wrong in practice: translate-only correction moved the boundary further from the real
    /// park on every one of four separate live measurements (16 m, 33 m, 21 m, 35 m — always
    /// west), because the read it was translating by was never reliable enough to correct against
    /// in the first place.
    /// </para>
    /// <para>
    /// The input changed, so the right thing to trust from it changed too. Gemini is no longer
    /// reading a photograph of an invisible property line — it is tracing a polygon Google's own
    /// roadmap renderer already drew, flat-coloured with a crisp edge. That is what the boundary
    /// actually is, not evidence pointing at it, so the traced shape becomes the final polygon
    /// directly rather than only steering a translation. See
    /// <c>docs/LOCATION_TO_BOUNDARY_END_TO_END.md</c> §9.7.
    /// </para>
    /// <para>
    /// Two gates, both of which must hold: the observed area is within
    /// <see cref="VisionCorrectionMinAreaRatio"/>x-<see cref="VisionCorrectionMaxAreaRatio"/>x of
    /// the mapped area (it is looking at the same site, not some other shaded shape on the map),
    /// and the observed centroid is inside the search radius (it did not trace something far
    /// away). There is no separate cap on how far the traced shape sits from the mapped one — a
    /// large shift is exactly what this exists to apply when OSM's ring is the one that is wrong.
    /// </para>
    /// </remarks>
    private static VisionCorrectedGeometry? TryBuildVisionTracedGeometry(
        ScoredBoundaryCandidate winner, BoundaryVisionAnalysis vision, GeoPoint center, BoundaryScoringOptions opts, string mappedSourceDetail)
    {
        if (vision.ObservedBoundary is not { Count: >= 3 } observed)
        {
            return null;
        }

        var observedArea = GeometryMath.AreaSquareMeters(observed);
        if (observedArea <= 0)
        {
            return null;
        }

        var mapped = winner.Candidate.Polygon.ExteriorRing;
        if (mapped.Count < 3)
        {
            return null;
        }

        var mappedArea = winner.Candidate.AreaSquareMeters;
        if (mappedArea > 0)
        {
            var ratio = observedArea / mappedArea;
            if (ratio < VisionCorrectionMinAreaRatio || ratio > VisionCorrectionMaxAreaRatio)
            {
                return null;
            }
        }

        var observedCentroid = GeometryMath.Centroid(observed);
        if (GeometryMath.DistanceMeters(observedCentroid, center) > opts.SearchRadiusMeters)
        {
            return null;
        }

        var mappedCentroid = GeometryMath.Centroid(mapped);
        var shiftMeters = GeometryMath.DistanceMeters(mappedCentroid, observedCentroid);

        return new VisionCorrectedGeometry(
            observed,
            observedArea,
            SiteBoundarySource.AiInterpretation,
            $"traced from Google's own map rendering of the site, {shiftMeters:F0} m from the OpenStreetMap-mapped position ({mappedSourceDetail})");
    }

    private sealed record VisionCorrectedGeometry(
        IReadOnlyList<GeoPoint> Polygon, double AreaSquareMeters, SiteBoundarySource Source, string SourceDetail);

    private static string? DescribeAiVerification(string agreement) => agreement switch
    {
        "agree" => "This was cross-checked against Google's own map rendering by Gemini vision analysis, which confirmed the same boundary.",
        "ai_override" => "Note: Gemini's map analysis pointed to a different boundary than map-data scoring alone suggested, and I went with Gemini's pick since it has direct visual evidence.",
        _ => null,
    };

    private sealed record FinalSelection(ScoredBoundaryCandidate Winner, double CombinedScore, string Agreement);

    private static ConfirmedSiteBoundaryData BuildManualFallback(string siteName, GeoPoint center)
    {
        var ring = GeometryMath.CirclePolygon(center, ManualFallbackRadiusMeters);
        var area = GeometryMath.AreaSquareMeters(ring);
        return new ConfirmedSiteBoundaryData(
            siteName, center.Latitude, center.Longitude, ring, area,
            Confidence: 0.0, BoundaryConfidenceLevel.Low, SiteBoundarySource.ManualFallback,
            SourceDetail: $"an approximate {ManualFallbackRadiusMeters:0} m buffer around the resolved location (no mapped boundary found)",
            AlternativeCandidateNames: []);
    }

    private static BoundaryConfidenceLevel ClassifyConfidence(double score, BoundaryScoringOptions opts) => score switch
    {
        _ when score >= opts.HighConfidenceThreshold => BoundaryConfidenceLevel.High,
        _ when score >= opts.MediumConfidenceThreshold => BoundaryConfidenceLevel.Medium,
        _ => BoundaryConfidenceLevel.Low,
    };

    private static readonly string[] DescriptiveTagKeys = ["leisure", "landuse", "amenity", "tourism", "natural", "building", "boundary"];

    private static string DescribeSource(BoundaryCandidate candidate)
    {
        if (candidate.Source != SiteBoundarySource.OsmBoundary)
        {
            return candidate.Source.ToString();
        }

        var tag = DescriptiveTagKeys
            .Select(key => candidate.Tags.TryGetValue(key, out var value) ? $"{key}={value}" : null)
            .FirstOrDefault(t => t is not null);

        return tag is null
            ? "OpenStreetMap"
            : $"OpenStreetMap ({tag})";
    }
}
