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

    public IReadOnlyList<GeoPoint> Join(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint) =>
        throw new NotImplementedException("Joining a footprint onto a hand-edited ring is built with specs/079 User Story 4.");

    public IReadOnlyList<GeoPoint> Cut(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint) =>
        throw new NotImplementedException("Cutting a footprint out of a hand-edited ring is built with specs/079 User Story 4.");

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

    private static double ShoelaceArea(IReadOnlyList<(double X, double Y)> ring)
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
