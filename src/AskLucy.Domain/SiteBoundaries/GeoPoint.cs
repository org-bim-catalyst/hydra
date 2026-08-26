namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — a single WGS84 coordinate. Kept as a plain value type
/// (no geometry library dependency, research.md #7) since this feature's needs (scoring
/// plausibility, rendering vertices) don't require survey-grade precision.
/// </summary>
public sealed record GeoPoint(double Latitude, double Longitude);
