using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution research.md #7 — lightweight geometry helpers (area,
/// centroid, distance) over an equirectangular local-meters projection around a reference
/// point. Deliberately not survey-grade (no geodesic/ellipsoidal math, no new geometry-library
/// dependency) — sufficient for scoring plausibility and rendering vertex offsets, the only two
/// things this feature needs geometry math for. Mirrors the reference notebook's own
/// non-geopandas fallback trade-off.
/// </summary>
public static class GeometryMath
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>Converts a <see cref="GeoPoint"/> to local meters (x = east, y = north) relative to <paramref name="reference"/>.</summary>
    public static (double X, double Y) ToLocalMeters(GeoPoint point, GeoPoint reference)
    {
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(DegreesToRadians(reference.Latitude));
        var x = (point.Longitude - reference.Longitude) * metersPerDegreeLongitude;
        var y = (point.Latitude - reference.Latitude) * MetersPerDegreeLatitude;
        return (x, y);
    }

    /// <summary>The simple average of a ring's vertices — not a true polygon centroid, but an adequate, honestly-approximate anchor point for scoring/rendering at site scale.</summary>
    public static GeoPoint Centroid(IReadOnlyList<GeoPoint> ring)
    {
        var latitude = ring.Average(p => p.Latitude);
        var longitude = ring.Average(p => p.Longitude);
        return new GeoPoint(latitude, longitude);
    }

    /// <summary>Polygon area in square meters via the shoelace formula over the ring's local-meters projection around its own centroid.</summary>
    public static double AreaSquareMeters(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3)
        {
            return 0.0;
        }

        var reference = Centroid(ring);
        var local = ring.Select(p => ToLocalMeters(p, reference)).ToList();

        var sum = 0.0;
        for (var i = 0; i < local.Count; i++)
        {
            var (x1, y1) = local[i];
            var (x2, y2) = local[(i + 1) % local.Count];
            sum += (x1 * y2) - (x2 * y1);
        }

        return Math.Abs(sum) / 2.0;
    }

    /// <summary>Straight-line distance in meters between two points, via the local-meters projection.</summary>
    public static double DistanceMeters(GeoPoint a, GeoPoint b)
    {
        var (x, y) = ToLocalMeters(a, b);
        return Math.Sqrt((x * x) + (y * y));
    }

    /// <summary>
    /// Compass bearing in degrees (0 = north, 90 = east, ...) from <paramref name="from"/> toward
    /// <paramref name="to"/>. Used to aim a Street View request from a perimeter viewpoint back at
    /// the site, so the fetched frame actually faces the boundary instead of pointing arbitrarily.
    /// </summary>
    public static double BearingDegrees(GeoPoint from, GeoPoint to)
    {
        var (east, north) = ToLocalMeters(to, from);
        var degrees = Math.Atan2(east, north) * 180.0 / Math.PI;
        return (degrees + 360.0) % 360.0;
    }

    /// <summary>(minLat, minLon, maxLat, maxLon) bounding box of a ring.</summary>
    public static (double MinLat, double MinLon, double MaxLat, double MaxLon) BoundingBox(IReadOnlyList<GeoPoint> ring) =>
        (ring.Min(p => p.Latitude), ring.Min(p => p.Longitude), ring.Max(p => p.Latitude), ring.Max(p => p.Longitude));

    /// <summary>
    /// A circular approximation around <paramref name="center"/> — the manual-fallback boundary
    /// shape used when no real candidate is found (FR-007), same idea as the reference notebook's
    /// own buffer-around-a-point fallback for an unmapped site.
    /// </summary>
    public static IReadOnlyList<GeoPoint> CirclePolygon(GeoPoint center, double radiusMeters, int segments = 24)
    {
        var points = new List<GeoPoint>(segments);
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(DegreesToRadians(center.Latitude));

        for (var i = 0; i < segments; i++)
        {
            var angle = 2 * Math.PI * i / segments;
            var x = radiusMeters * Math.Cos(angle);
            var y = radiusMeters * Math.Sin(angle);
            var latitude = center.Latitude + (y / MetersPerDegreeLatitude);
            var longitude = center.Longitude + (x / metersPerDegreeLongitude);
            points.Add(new GeoPoint(latitude, longitude));
        }

        return points;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
