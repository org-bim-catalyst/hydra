using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Overpass;

/// <summary>
/// specs/089-site-geometry-specialist — the ring assembly SPEC-042's boundary search used, moved
/// here unchanged so the shared <see cref="OverpassFeatureQueryProvider"/> can return a relation as
/// one polygon rather than as unordered member ways, and so the boundary provider keeps its exact
/// previous behaviour.
/// </summary>
internal static class OverpassGeometry
{
    /// <summary>Whether the geometry's first and last points coincide — the closing point may or may not be repeated.</summary>
    internal static bool IsClosedRing(IReadOnlyList<GeoPoint> geometry) =>
        geometry.Count >= 4 && SamePoint(geometry[0], geometry[^1]);

    // Member ways of a relation share their end nodes, so matching coordinates are exact copies.
    internal static bool SamePoint(GeoPoint a, GeoPoint b) =>
        Math.Abs(a.Latitude - b.Latitude) < 1e-9 && Math.Abs(a.Longitude - b.Longitude) < 1e-9;

    /// <summary>
    /// Joins a relation's outer member ways end to end into closed rings and returns the largest.
    /// OSM splits long outlines across several ways, in no particular order or direction, so each
    /// step takes whichever unused way starts or ends where the ring currently ends, reversing it
    /// if needed. A chain that cannot close is dropped rather than force-closed — a straight line
    /// across a gap would be an invented edge. Holes (inner members) are ignored: the candidate is
    /// the site's outline.
    /// </summary>
    internal static List<GeoPoint>? AssembleLargestOuterRing(IReadOnlyList<IReadOnlyList<GeoPoint>> outerSegments)
    {
        var segments = outerSegments
            .Where(segment => segment.Count >= 2)
            .Select(segment => segment.ToList())
            .ToList();

        List<GeoPoint>? largest = null;
        var largestArea = 0.0;
        while (segments.Count > 0)
        {
            var ring = segments[0];
            segments.RemoveAt(0);

            while (!IsClosedRing(ring))
            {
                var nextIndex = segments.FindIndex(s => SamePoint(s[0], ring[^1]) || SamePoint(s[^1], ring[^1]));
                if (nextIndex < 0)
                {
                    break;
                }

                var next = segments[nextIndex];
                segments.RemoveAt(nextIndex);
                if (!SamePoint(next[0], ring[^1]))
                {
                    next.Reverse();
                }

                ring.AddRange(next.Skip(1));
            }

            if (!IsClosedRing(ring))
            {
                continue;
            }

            var area = GeometryMath.AreaSquareMeters(ring);
            if (area > largestArea)
            {
                largest = ring;
                largestArea = area;
            }
        }

        return largest;
    }
}
