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
        var winner = ranked[0];
        var confidenceLevel = ClassifyConfidence(winner.Score, opts);

        var alternativeNames = ranked
            .Skip(1)
            .Where(c => winner.Score - c.Score <= AlternativeCandidateScoreMargin && !string.IsNullOrWhiteSpace(c.Candidate.Name))
            .Select(c => c.Candidate.Name)
            .Distinct()
            .ToList();

        var sourceDetail = DescribeSource(winner.Candidate);
        var confirmedBoundary = new ConfirmedSiteBoundaryData(
            confirmedLocation.LocationName,
            center.Latitude, center.Longitude,
            winner.Candidate.Polygon.ExteriorRing,
            winner.Candidate.AreaSquareMeters,
            winner.Score, confidenceLevel,
            winner.Candidate.Source, sourceDetail,
            alternativeNames);

        var confirmationText = BoundaryConfirmationTemplates.WithAlternatives(
            BoundaryConfirmationTemplates.Confirmed(confirmedLocation.LocationName, confidenceLevel, sourceDetail),
            alternativeNames);

        BoundaryResolutionServiceLog.Resolved(logger, userChatId, BoundaryResolutionOutcomeType.Confirmed, confirmedLocation.LocationName);
        return new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, confirmedBoundary, confirmationText);
    }

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
