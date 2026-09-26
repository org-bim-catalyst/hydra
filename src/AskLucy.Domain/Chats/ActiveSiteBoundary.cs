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
    string SourceDetail)
{
    /// <summary>
    /// specs/077 — the site's own outline before any <see cref="Members"/> were joined to it. Null
    /// when nothing was ever joined, which is every boundary stored before specs/077: then
    /// <see cref="Polygon"/> is the site's own outline.
    /// </summary>
    public IReadOnlyList<GeoPoint>? CorePolygon { get; init; }

    /// <summary>specs/077 — outlines of included members that stand apart from <see cref="Polygon"/>, e.g. a tower across the street.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> AdditionalPolygons { get; init; } = [];

    /// <summary>specs/077 — buildings carrying the site's name, and whether the user included each.</summary>
    public IReadOnlyList<SiteBoundaryMember> Members { get; init; } = [];
}
