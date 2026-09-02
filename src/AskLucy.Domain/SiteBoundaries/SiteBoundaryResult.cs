namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution data-model.md — the full result of one boundary
/// resolution: the shape, its confidence, its source, and a plain-language explanation
/// (<see cref="Notes"/>, always non-empty per FR-005 — a boundary is never shown without a
/// stated source). <see cref="AlternativeCandidateNames"/> is non-empty only when FR-008's
/// disclosure applies (two or more similarly-plausible candidates existed).
/// </summary>
public sealed record SiteBoundaryResult(
    string SiteName,
    GeoPoint Centroid,
    SiteBoundaryPolygon Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> AlternativeCandidateNames);
