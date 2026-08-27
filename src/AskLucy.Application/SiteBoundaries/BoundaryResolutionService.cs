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
        Message = "Boundary resolution for chat {UserChatId}: Gemini reported an observed boundary but it failed the plausibility check, so the mapped geometry was kept")]
    public static partial void VisionCorrectionRejected(ILogger logger, Guid userChatId);
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
        var visionAnalysis = await AnalyzeWithVisionAsync(center, ranked, confirmedLocation.LocationName, opts, cancellationToken);
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

        var mappedSourceDetail = DescribeSource(winner.Candidate);
        var visionGeometry = TryBuildVisionCorrectedGeometry(winner, visionAnalysis, center, opts, mappedSourceDetail);
        if (visionAnalysis.ObservedBoundary is not null && visionGeometry is null)
        {
            BoundaryResolutionServiceLog.VisionCorrectionRejected(logger, userChatId);
        }

        var finalPolygon = visionGeometry?.Polygon ?? winner.Candidate.Polygon.ExteriorRing;
        var finalArea = visionGeometry?.AreaSquareMeters ?? winner.Candidate.AreaSquareMeters;
        var finalSource = visionGeometry?.Source ?? winner.Candidate.Source;
        var sourceDetail = visionGeometry?.SourceDetail ?? mappedSourceDetail;

        var confirmedBoundary = new ConfirmedSiteBoundaryData(
            confirmedLocation.LocationName,
            center.Latitude, center.Longitude,
            finalPolygon, finalArea,
            selection.CombinedScore, confidenceLevel,
            finalSource, sourceDetail,
            alternativeNames);

        var aiNote = visionGeometry is not null
            ? "Note: satellite-image analysis found this boundary's mapped position looked shifted from what's actually visible, so I used Gemini's own visual read of the boundary instead of the raw map data."
            : DescribeAiVerification(selection.Agreement);
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
        BoundaryScoringOptions opts, CancellationToken cancellationToken)
    {
        if (!opts.EnableAiVisionVerification)
        {
            return BoundaryVisionAnalysis.NotConfigured(
                "AI vision analysis is disabled (BoundaryScoring:EnableAiVisionVerification = false).");
        }

        var image = await satelliteImageProvider.FetchAsync(center, opts.SearchRadiusMeters, cancellationToken);
        if (image is null)
        {
            return BoundaryVisionAnalysis.NotConfigured("No satellite image available to analyze this run.");
        }

        return await visionAnalyzer.AnalyzeAsync(image, ranked, siteName, center, cancellationToken);
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

    /// <summary>Plausibility gate for <see cref="BoundaryVisionAnalysis.ObservedBoundary"/> — the area and position must be broadly consistent with the mapped candidate it's meant to be describing, or it's discarded as an unreliable read rather than trusted at face value.</summary>
    private const double VisionCorrectionMinAreaRatio = 0.3;

    private const double VisionCorrectionMaxAreaRatio = 3.0;

    /// <summary>
    /// User-requested departure from the notebook's "AI never invents coordinates" rule: when a
    /// site has only one real OSM candidate, "picking a better candidate" (<see cref="SelectFinalCandidate"/>)
    /// has nothing else to pick even if that one candidate's own geometry is positionally wrong
    /// (observed live: Al Safa Park 2's OSM way is shaped correctly but shifted from its true
    /// position, likely inherited from misaligned source imagery used when it was originally
    /// traced). Gemini's own visual read of the boundary (<see cref="BoundaryVisionAnalysis.ObservedBoundary"/>,
    /// already geo-referenced from image-relative coordinates by <see cref="IBoundaryVisionAnalyzer"/>)
    /// is trusted to override the mapped geometry ONLY after passing a plausibility check — area
    /// within <see cref="VisionCorrectionMinAreaRatio"/>x-<see cref="VisionCorrectionMaxAreaRatio"/>x
    /// of the mapped candidate's area, and centroid within the search radius of the resolved
    /// center — so an unrelated or hallucinated read can't silently replace real mapped geometry.
    /// </summary>
    private static VisionCorrectedGeometry? TryBuildVisionCorrectedGeometry(
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

        return new VisionCorrectedGeometry(
            observed, observedArea, SiteBoundarySource.AiInterpretation,
            $"Gemini vision's own read of the satellite image (cross-checked against {mappedSourceDetail}, area within tolerance)");
    }

    private sealed record VisionCorrectedGeometry(
        IReadOnlyList<GeoPoint> Polygon, double AreaSquareMeters, SiteBoundarySource Source, string SourceDetail);

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
