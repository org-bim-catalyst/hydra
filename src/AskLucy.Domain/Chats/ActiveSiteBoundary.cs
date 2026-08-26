using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Domain.Chats;

/// <summary>
/// specs/042-site-boundary-resolution — the currently confirmed site boundary the viewer has
/// highlighted, mirroring <see cref="ActiveSiteLocation"/> exactly (research.md #10/#11).
/// Owned by <see cref="UserChat"/> so it survives the turn boundary; a repeated reference to the
/// same site reuses this value instead of forcing a fresh resolution (FR-009).
/// </summary>
public sealed record ActiveSiteBoundary(
    string SiteName,
    double CentroidLatitude,
    double CentroidLongitude,
    IReadOnlyList<GeoPoint> Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail);
