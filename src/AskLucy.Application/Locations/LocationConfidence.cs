using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Locations;

/// <summary>
/// The single place a confirmed location's user-facing Low/Medium/High level is decided — the
/// same scale the studio's site card shows for a boundary, so the card can appear the moment the
/// place is confirmed rather than only once its outline resolves, and Lucy's own words about it
/// ("confirmed with high confidence") come from the same rule the card reads.
///
/// <para>
/// <b>Decided from the geocoder's precision, never from <c>Confidence</c>.</b> A location's
/// numeric confidence is the winning candidate's <c>Importance</c>, whose scale depends on which
/// geocoder answered (Google maps <c>location_type</c> into 0.40–0.90; Nominatim passes through a
/// Wikipedia-popularity float where a correctly-matched local park scores ~0.08) — thresholding it
/// would call every Nominatim answer Low. See <c>SiteAnalysisConfidence</c> for the same rule.
/// </para>
/// </summary>
public static class LocationConfidence
{
    /// <summary>
    /// <paramref name="locationType"/> is the geocoder's own precision code (Google's
    /// <c>location_type</c>). Null when the geocoder reports none (Nominatim) or for a location
    /// recorded before the code was kept: the place still passed the importance floor and the
    /// dominance check, so it is a single intended place of unstated precision — Medium.
    /// </summary>
    public static BoundaryConfidenceLevel Classify(string? locationType) => locationType switch
    {
        "ROOFTOP" => BoundaryConfidenceLevel.High,
        "RANGE_INTERPOLATED" or "GEOMETRIC_CENTER" => BoundaryConfidenceLevel.Medium,
        "APPROXIMATE" => BoundaryConfidenceLevel.Low,
        _ => BoundaryConfidenceLevel.Medium,
    };
}
