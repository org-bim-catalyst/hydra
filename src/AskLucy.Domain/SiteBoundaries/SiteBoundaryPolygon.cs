namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — a site's exterior boundary ring in WGS84. Not
/// necessarily explicitly closed (first point need not repeat as the last) — callers that need
/// a closed ring (e.g. rendering) close it themselves; this keeps the type usable regardless of
/// which convention a given candidate source returns.
/// </summary>
public sealed record SiteBoundaryPolygon(IReadOnlyList<GeoPoint> ExteriorRing);
