using System.ComponentModel.DataAnnotations;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — deterministic scoring configuration, ported from the
/// reference notebook's <c>SITE_BOUNDARY_CONFIG</c>. Bound from the "BoundaryScoring"
/// appsettings section. The five <c>*Weight</c> properties and the two confidence thresholds
/// are validated via <see cref="IValidatableObject"/> (constitution §4: bound to
/// <c>IOptions&lt;T&gt;</c>, validated at startup) — a cross-field "weights sum to 1.0"
/// invariant can't be expressed with a single <see cref="RangeAttribute"/> alone.
/// </summary>
public sealed class BoundaryScoringOptions : IValidatableObject
{
    public const string SectionName = "BoundaryScoring";

    [Range(1, int.MaxValue)]
    public int SearchRadiusMeters { get; set; } = 500;

    [Range(1, int.MaxValue)]
    public int MaxCandidates { get; set; } = 10;

    [Range(0.0, 1.0)]
    public double HighConfidenceThreshold { get; set; } = 0.85;

    [Range(0.0, 1.0)]
    public double MediumConfidenceThreshold { get; set; } = 0.65;

    [Range(0.0, 1.0)]
    public double SourceReliabilityWeight { get; set; } = 0.35;

    [Range(0.0, 1.0)]
    public double NameMatchWeight { get; set; } = 0.20;

    [Range(0.0, 1.0)]
    public double GeometryQualityWeight { get; set; } = 0.15;

    [Range(0.0, 1.0)]
    public double CenterProximityWeight { get; set; } = 0.20;

    [Range(0.0, 1.0)]
    public double LandUseAgreementWeight { get; set; } = 0.10;

    /// <summary>
    /// specs/042-site-boundary-resolution — the reference notebook's <c>SITE_BOUNDARY_CONFIG["enable_ai"]</c>,
    /// gating the Gemini vision cross-check (<see cref="IBoundaryVisionAnalyzer"/>) that
    /// disambiguates between multiple plausible OSM candidates using satellite imagery instead of
    /// tag/geometry heuristics alone. Defaults to enabled here (the notebook itself defaults it to
    /// <c>False</c> only because no vision API was wired into that notebook run) — a missing
    /// Gemini credential still degrades this gracefully to the deterministic score alone via
    /// <see cref="BoundaryVisionAnalysis.NotConfigured"/>, never an error.
    /// </summary>
    public bool EnableAiVisionVerification { get; set; } = true;

    /// <summary>
    /// specs/043 FR-034 - the time budget for one Gemini vision call, in seconds.
    /// <para>
    /// The analyzer previously inherited the shared GoogleGemini HttpClient timeout of two
    /// minutes, so a hung vision call stalled an interactive boundary resolution for that long
    /// before falling back. The fallback was correct; it was simply far too late.
    /// </para>
    /// <para>
    /// 30s rather than something tighter because this host has twice produced false
    /// "unavailable" results from 15s timeouts (Overpass and Geocoding were both widened to 30s
    /// after exactly that), and a multimodal call carrying a base64 satellite image is heavier
    /// than either. Too short a budget would manufacture the very failure US5 exists to survive.
    /// </para>
    /// </summary>
    [Range(1, 300)]
    public int VisionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// specs/044-location-viewer-regression FR-003 — the aggregate budget for one whole
    /// boundary-resolution step, in seconds.
    /// <para>
    /// <see cref="VisionTimeoutSeconds"/> bounds a single external call; this bounds the entire
    /// pipeline. Per-dependency timeouts alone do not, because they sum: Overpass (30s) + ESRI
    /// imagery (30s) + vision (30s) is a ~90s worst case with nothing capping the total, which
    /// left the chat turn — and, before this feature's reordering, the viewer update itself —
    /// hostage to it.
    /// </para>
    /// <para>
    /// 45s rather than 30s: a slow-but-healthy Overpass run can consume 30s on its own, so a
    /// 30s aggregate would abandon boundaries that were about to succeed and manufacture the
    /// false "unavailable" this host has already produced twice. Typical end-to-end runs land
    /// at 10–30s, so 45s clears the normal case with headroom while halving the pathological one.
    /// </para>
    /// </summary>
    [Range(1, 300)]
    public int BoundaryTimeoutSeconds { get; set; } = 45;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var weightSum = SourceReliabilityWeight + NameMatchWeight + GeometryQualityWeight
            + CenterProximityWeight + LandUseAgreementWeight;
        if (Math.Abs(weightSum - 1.0) > 1e-6)
        {
            yield return new ValidationResult(
                $"BoundaryScoring weights must sum to 1.0 (currently {weightSum}).",
                [nameof(SourceReliabilityWeight), nameof(NameMatchWeight), nameof(GeometryQualityWeight), nameof(CenterProximityWeight), nameof(LandUseAgreementWeight)]);
        }

        if (HighConfidenceThreshold <= MediumConfidenceThreshold)
        {
            yield return new ValidationResult(
                $"{nameof(HighConfidenceThreshold)} ({HighConfidenceThreshold}) must be greater than {nameof(MediumConfidenceThreshold)} ({MediumConfidenceThreshold}).",
                [nameof(HighConfidenceThreshold), nameof(MediumConfidenceThreshold)]);
        }

        // specs/044 FR-003: the aggregate budget must leave room for the vision call it contains.
        // Configured the other way round, vision could never finish inside the budget and would be
        // silently disabled in production — the exact class of quiet degradation constitution §VIII
        // forbids. Failing at startup makes the misconfiguration loud instead.
        if (BoundaryTimeoutSeconds <= VisionTimeoutSeconds)
        {
            yield return new ValidationResult(
                $"{nameof(BoundaryTimeoutSeconds)} ({BoundaryTimeoutSeconds}) must be greater than {nameof(VisionTimeoutSeconds)} ({VisionTimeoutSeconds}), " +
                "or AI vision verification can never complete within the boundary step's budget.",
                [nameof(BoundaryTimeoutSeconds), nameof(VisionTimeoutSeconds)]);
        }
    }
}
