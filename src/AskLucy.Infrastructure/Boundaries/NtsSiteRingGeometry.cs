using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// specs/079 (research D3) — <see cref="ISiteRingGeometry"/> on NetTopologySuite. Every operation
/// works in a local metric frame (x = east, y = north, in metres) around the rings' own reference
/// point, the same projection <see cref="GeometryMath"/> uses, so distances and areas are in
/// metres and agree with the rest of the boundary code. The library never leaves this class.
/// </summary>
public sealed class NtsSiteRingGeometry : ISiteRingGeometry
{
    private const double MinimumAreaSquareMeters = 1.0;
    private const double DuplicateCornerMeters = 0.05;
    private const double MetersPerDegreeLatitude = 111_320.0;

    private static readonly GeometryFactory Factory = new();

    public RingValidationResult Validate(IReadOnlyList<GeoPoint> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var corners = WithoutClosingCorner(ring);
        if (corners.Count < 3)
        {
            return RingValidationResult.Degenerate;
        }

        var reference = corners[0];
        var local = corners.Select(c => GeometryMath.ToLocalMeters(c, reference)).ToList();

        if (HasDuplicateCorner(local))
        {
            return RingValidationResult.DuplicateCorner;
        }

        // A ring whose hull is under a square metre is collapsed (collinear or tiny). Checked before
        // simplicity because collinear corners overlap themselves and would read as crossing.
        var linear = ToLinearRing(local);
        if (linear.ConvexHull().Area < MinimumAreaSquareMeters)
        {
            return RingValidationResult.Degenerate;
        }

        // Simplicity before the signed area: a bow-tie's two lobes cancel in it, so it would
        // otherwise be reported as degenerate rather than as crossing itself.
        if (!linear.IsSimple)
        {
            return RingValidationResult.SelfCrossing;
        }

        return Math.Abs(ShoelaceArea(local)) < MinimumAreaSquareMeters
            ? RingValidationResult.Degenerate
            : RingValidationResult.Ok;
    }

    public double UnionArea(IReadOnlyList<IReadOnlyList<GeoPoint>> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        if (rings.Count == 0)
        {
            return 0;
        }

        var reference = rings[0][0];
        var union = UnaryUnionOp.Union(rings.Select(r => ToPolygon(r, reference)).Cast<Geometry>().ToList());
        return union.Area;
    }

    public bool Intersects(
        IReadOnlyList<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<IReadOnlyList<GeoPoint>> foundRings, double growMeters)
    {
        ArgumentNullException.ThrowIfNull(rings);
        ArgumentNullException.ThrowIfNull(foundRings);
        if (rings.Count == 0 || foundRings.Count == 0)
        {
            return false;
        }

        var reference = foundRings[0][0];
        var found = UnaryUnionOp.Union(foundRings.Select(r => ToPolygon(r, reference)).Cast<Geometry>().ToList());
        var grown = growMeters > 0 ? found.Buffer(growMeters) : found;
        return rings.Any(r => grown.Intersects(ToPolygon(r, reference)));
    }

    public CombineResult Combine(
        IReadOnlyList<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<GeoPoint> shape, CombineOperation operation)
    {
        ArgumentNullException.ThrowIfNull(rings);
        ArgumentNullException.ThrowIfNull(shape);
        if (rings.Count == 0)
        {
            return new CombineResult(CombineFailure.NothingLeft, []);
        }

        var reference = rings[0][0];
        var current = UnaryUnionOp.Union(rings.Select(r => ToPolygon(r, reference)).Cast<Geometry>().ToList());
        var shapePolygon = ToPolygon(shape, reference);
        var combined = operation == CombineOperation.Add ? current.Union(shapePolygon) : current.Difference(shapePolygon);

        // Slivers left over by a cut are not outlines; anything under a square metre is dropped.
        var polygons = Polygons(combined).Where(p => p.Area >= MinimumAreaSquareMeters).ToList();
        if (polygons.Count == 0)
        {
            return new CombineResult(CombineFailure.NothingLeft, []);
        }

        if (polygons.Any(p => p.NumInteriorRings > 0))
        {
            return new CombineResult(CombineFailure.HoleNotSupported, []);
        }

        // The ring holding the original first ring stays first; the rest follow, largest first.
        var anchor = ToPolygon(rings[0], reference).InteriorPoint;
        var ordered = polygons
            .OrderByDescending(p => p.Contains(anchor) || p.Intersects(anchor))
            .ThenByDescending(p => p.Area)
            .ToList();

        IReadOnlyList<IReadOnlyList<GeoPoint>> result =
        [
            .. ordered.Select(p => (IReadOnlyList<GeoPoint>)fromShell(p, reference)),
        ];
        return new CombineResult(CombineFailure.None, result);

        static List<GeoPoint> fromShell(Polygon polygon, GeoPoint reference)
        {
            // The shell repeats its first coordinate at the end; rings here are open. Local metres back to degrees.
            var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(reference.Latitude * Math.PI / 180);
            var coordinates = polygon.ExteriorRing.Coordinates;
            return
            [
                .. coordinates.Take(coordinates.Length - 1).Select(c => new GeoPoint(
                    reference.Latitude + (c.Y / MetersPerDegreeLatitude),
                    reference.Longitude + (c.X / metersPerDegreeLongitude))),
            ];
        }
    }

    private static IEnumerable<Polygon> Polygons(Geometry geometry) => geometry switch
    {
        Polygon polygon => [polygon],
        GeometryCollection collection => collection.Geometries.SelectMany(Polygons),
        _ => [],
    };

    /// <summary>A gap this small between the ring and a footprint is closed by a join (OSM leaves BurJuman's hotel 0.5 m off the mall).</summary>
    private const double SeamMeters = 1.25;

    /// <summary>A cut leaves no sliver thinner than this near the seam.</summary>
    private const double SliverMeters = 0.5;

    public IReadOnlyList<GeoPoint> Join(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint)
    {
        ArgumentNullException.ThrowIfNull(editedRing);
        ArgumentNullException.ThrowIfNull(footprint);

        var reference = editedRing[0];
        var edited = ToPolygon(editedRing, reference);
        var added = ToPolygon(footprint, reference);

        // edited + footprint + the ground between them: only the seam is new, so every other corner stays where it was.
        var seam = edited.Buffer(SeamMeters).Intersection(added.Buffer(SeamMeters));
        var joined = UnaryUnionOp.Union(new List<Geometry> { edited, added, seam });

        return LargestShell(joined, reference);
    }

    public IReadOnlyList<GeoPoint> Cut(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint)
    {
        ArgumentNullException.ThrowIfNull(editedRing);
        ArgumentNullException.ThrowIfNull(footprint);

        var reference = editedRing[0];
        var edited = ToPolygon(editedRing, reference);
        var removed = ToPolygon(footprint, reference);

        var remaining = edited.Difference(removed);

        // Whatever thin strip the seam left beside the footprint is smoothed away, and only there.
        var near = removed.Buffer(SeamMeters + SliverMeters);
        var opened = remaining.Buffer(-SliverMeters / 2).Buffer(SliverMeters / 2);
        var result = remaining.Difference(near).Union(opened.Intersection(near));

        return LargestShell(result, reference);
    }

    private static List<GeoPoint> LargestShell(Geometry geometry, GeoPoint reference)
    {
        // Buffering leaves clusters of corners a few millimetres apart; 6 cm folds them into one without moving a placed corner.
        var polygon = Polygons(NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(geometry, DuplicateCornerMeters * 1.2))
            .OrderByDescending(p => p.Area).FirstOrDefault()
            ?? throw new InvalidOperationException("The result has no area.");
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(reference.Latitude * Math.PI / 180);
        var coordinates = polygon.ExteriorRing.Coordinates;
        return
        [
            .. coordinates.Take(coordinates.Length - 1).Select(c => new GeoPoint(
                reference.Latitude + (c.Y / MetersPerDegreeLatitude),
                reference.Longitude + (c.X / metersPerDegreeLongitude))),
        ];
    }

    private static IReadOnlyList<GeoPoint> WithoutClosingCorner(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring.Take(ring.Count - 1).ToList() : ring;

    private static Polygon ToPolygon(IReadOnlyList<GeoPoint> ring, GeoPoint reference)
    {
        var local = WithoutClosingCorner(ring).Select(c => GeometryMath.ToLocalMeters(c, reference)).ToList();
        return Factory.CreatePolygon(ToLinearRing(local));
    }

    private static LinearRing ToLinearRing(IReadOnlyList<(double X, double Y)> local)
    {
        var coordinates = local.Select(p => new Coordinate(p.X, p.Y)).ToList();
        coordinates.Add(coordinates[0]);
        return Factory.CreateLinearRing(coordinates.ToArray());
    }

    private static double ShoelaceArea(List<(double X, double Y)> ring)
    {
        var sum = 0.0;
        for (var i = 0; i < ring.Count; i++)
        {
            var (x1, y1) = ring[i];
            var (x2, y2) = ring[(i + 1) % ring.Count];
            sum += (x1 * y2) - (x2 * y1);
        }

        return sum / 2.0;
    }

    /// <summary>Any two corners closer than <see cref="DuplicateCornerMeters"/>, found with a grid so a 2,000-corner ring stays linear.</summary>
    private static bool HasDuplicateCorner(IReadOnlyList<(double X, double Y)> ring)
    {
        var cells = new Dictionary<(long, long), List<(double X, double Y)>>();
        foreach (var point in ring)
        {
            var cx = (long)Math.Floor(point.X / DuplicateCornerMeters);
            var cy = (long)Math.Floor(point.Y / DuplicateCornerMeters);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (!cells.TryGetValue((cx + dx, cy + dy), out var neighbours))
                    {
                        continue;
                    }

                    if (neighbours.Any(n => Math.Sqrt(Math.Pow(n.X - point.X, 2) + Math.Pow(n.Y - point.Y, 2)) < DuplicateCornerMeters))
                    {
                        return true;
                    }
                }
            }

            if (!cells.TryGetValue((cx, cy), out var own))
            {
                cells[(cx, cy)] = own = [];
            }

            own.Add(point);
        }

        return false;
    }
}
