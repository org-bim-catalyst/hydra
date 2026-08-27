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
    }
}
