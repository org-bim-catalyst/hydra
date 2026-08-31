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
public sealed class BoundaryResolutionService(
    IBoundaryCandidateProvider candidateProvider,
    BoundaryCandidateScorer scorer,
    ISatelliteImageProvider satelliteImageProvider,
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

        // The mapped ring is used exactly as OSM has it.
        //
        // Two repositioning steps used to run here and both made the outline worse. Measured
        // against Google's own basemap — the one the viewer actually draws on — OSM's ring for
        // Al Safa Park 2 already lands on Google's park polygon to within a few metres. There was
        // no offset to correct; the offset the user was seeing was the correction itself.
        //
        // What each one did wrong:
        //  - Basemap alignment snapped the ring's centroid onto the geocoded point. For a park
        //    Google returns an establishment POI (location_type ROOFTOP, bounds null) — a marker
        //    placed inside the polygon, not its centre. Snapping to it dragged a correct ring
        //    24 m north-west.
        //  - The vision correction translated the ring onto Gemini's read of an ESRI World
        //    Imagery crop, which is a different frame again, on top of a shape that was already
        //    in the right place.
        //
        // Gemini's read is still used, for the thing it is genuinely good at: picking which
        // candidate is the site (SelectFinalCandidate above). It no longer moves geometry.
        var sourceDetail = DescribeSource(winner.Candidate);
        var finalPolygon = winner.Candidate.Polygon.ExteriorRing;
        var finalArea = winner.Candidate.AreaSquareMeters;
        var finalSource = winner.Candidate.Source;

        var confirmedBoundary = new ConfirmedSiteBoundaryData(
            confirmedLocation.LocationName,
            center.Latitude, center.Longitude,
            finalPolygon, finalArea,
            selection.CombinedScore, confidenceLevel,
            finalSource, sourceDetail,
            alternativeNames);

        var aiNote = DescribeAiVerification(selection.Agreement);
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
            var image = await satelliteImageProvider.FetchAsync(center, opts.SearchRadiusMeters, cancellationToken);
            if (image is null)
            {
                return BoundaryVisionAnalysis.NotConfigured("No satellite image available to analyze this run.");
            }

            return await visionAnalyzer.AnalyzeAsync(image, ranked, siteName, center, cancellationToken);
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

    private static string? DescribeAiVerification(string agreement) => agreement switch
    {
        "agree" => "This was cross-checked against satellite imagery by Gemini vision analysis, which confirmed the same boundary.",
        "ai_override" => "Note: satellite-image analysis by Gemini pointed to a different boundary than map-data scoring alone suggested, and I went with Gemini's pick since it has direct visual evidence.",
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
