namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/079 — the outline exactly as Lucy found it (with the building choice in force), kept on a
/// <see cref="SiteBoundaryCorrection"/> so a chat can fill its own boundary from the correction
/// without resolving, and so a reset can restore it.
/// </summary>
public sealed record FoundSiteBoundarySnapshot(
    IReadOnlyList<GeoPoint> Polygon,
    IReadOnlyList<IReadOnlyList<GeoPoint>> AdditionalPolygons,
    IReadOnlyList<GeoPoint>? CorePolygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,
    IReadOnlyList<SiteBoundaryMember> Members);
