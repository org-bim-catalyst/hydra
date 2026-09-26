using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/052-solar-analysis research D8, restated by specs/053-rendered-building-footprints
/// (FR-011/FR-019) — the stated rule for which retrieved footprint is "the site's own": the
/// polygon containing the site point (first match in the caller's own return order), then the
/// footprint whose edge is nearest the site point within <see cref="EdgeToleranceMetres"/>,
/// otherwise none. Extracted from <c>OverpassBuildingFootprintProvider</c> (its original, sole
/// implementation) so both building-footprint sources apply the identical rule rather than two
/// copies that could drift apart — the DRY concern constitution §2.III forbids for business logic,
/// as opposed to superficially similar but unrelated code.
/// </summary>
internal static class SiteBuildingLocator
{
    private const double EdgeToleranceMetres = 25.0;

    public static int FindIndex(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, GeoPoint sitePoint)
    {
        for (var i = 0; i < rings.Count; i++)
        {
            if (GeometryMath.Contains(rings[i], sitePoint)) return i;
        }

        var nearestIndex = -1;
        var nearestDistance = double.MaxValue;
        for (var i = 0; i < rings.Count; i++)
        {
            var distance = DistanceToRingEdge(sitePoint, rings[i]);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestDistance <= EdgeToleranceMetres ? nearestIndex : -1;
    }

    private static double DistanceToRingEdge(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        var reference = point;
        var (px, py) = GeometryMath.ToLocalMeters(point, reference); // (0, 0)
        var local = ring.Select(p => GeometryMath.ToLocalMeters(p, reference)).ToList();

        var minDistance = double.MaxValue;
        for (var i = 0; i < local.Count; i++)
        {
            var a = local[i];
            var b = local[(i + 1) % local.Count];
            var distance = DistancePointToSegment(px, py, a.X, a.Y, b.X, b.Y);
            if (distance < minDistance) minDistance = distance;
        }
        return minDistance;
    }

    private static double DistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        var abx = bx - ax;
        var aby = by - ay;
        var lengthSquared = (abx * abx) + (aby * aby);
        var t = lengthSquared < 1e-9 ? 0 : Math.Clamp((((px - ax) * abx) + ((py - ay) * aby)) / lengthSquared, 0, 1);
        var closestX = ax + (t * abx);
        var closestY = ay + (t * aby);
        var dx = px - closestX;
        var dy = py - closestY;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
