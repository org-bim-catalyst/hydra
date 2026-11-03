using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// specs/089-site-geometry-specialist data-model.md § 4 — measures a site from its resolved shape
/// and the streets around it. Pure arithmetic over geometry the caller already holds: no I/O, no
/// source access, no confidence judgement (that is the tool's, through
/// <see cref="SiteAnalysisConfidence"/>). Every constant is named here so the finding's method is
/// inspectable rather than implied.
/// </summary>
public sealed class SiteGeometryCalculator
{
    // CA1822: kept instance rather than static — this is a DI-registered service consumed as a
    // constructor dependency (SiteGeometryTool), matching SiteAnalysisContentComposer's own
    // precedent. A static calculator would be a static service class, which the constitution rules out.
#pragma warning disable CA1822

    /// <summary>
    /// How far a boundary edge may sit from a street centreline and still count as fronting it.
    /// Covers OSM centreline-versus-parcel-edge offsets across a verge or service strip; a shorter
    /// tolerance would silently drop real frontages on wide roads.
    /// </summary>
    internal const double FrontageToleranceMeters = 25.0;

    /// <summary>Above this width-to-length ratio the site has no defensible principal direction.</summary>
    internal const double NearSquareRatio = 0.95;

    /// <summary>How far the platform's 24-vertex fallback buffer's vertices may stray from its mean radius.</summary>
    private const double BufferRadiusTolerance = 0.005;

    private const int BufferVertexCount = 24;

    /// <summary>
    /// The recorded parcel that contains the analysed point, smallest first. A parcel that does not
    /// contain the site's point is a neighbour, not this site's parcel; nested outlines resolve to
    /// the most specific one.
    /// </summary>
    public SiteGeometryParcel? FindParcel(IReadOnlyList<SiteGeometryParcel> parcels, GeoPoint sitePoint) =>
        parcels
            .Where(parcel => parcel.Ring.Count >= 3 && GeometryMath.Contains(parcel.Ring, sitePoint))
            .OrderBy(parcel => GeometryMath.AreaSquareMeters(parcel.Ring))
            .FirstOrDefault();

    public SiteGeometryMeasurements Measure(
        IReadOnlyList<GeoPoint> ring, IReadOnlyList<SiteGeometryStreet> streets)
    {
        var area = GeometryMath.AreaSquareMeters(ring);
        var (length, width, bearing) = Dimensions(ring);
        var frontages = Frontages(ring, streets);

        return new SiteGeometryMeasurements(area, length, width, bearing, frontages, LooksLikeResolvedBuffer(ring));
    }

    /// <summary>
    /// Principal-axis extents: projection of the vertices onto the axis of greatest variance and
    /// onto its perpendicular, larger extent first. A latitude/longitude bounding box would overstate
    /// every site that is not axis-aligned.
    /// </summary>
    private static (double Length, double Width, double? BearingDegrees) Dimensions(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count < 3)
        {
            return (0.0, 0.0, null);
        }

        var reference = GeometryMath.Centroid(ring);

        // A closed ring repeats its first point, and a duplicated vertex biases the covariance
        // toward that corner: a 100 x 60 rectangle then reports an 81° axis and 108 x 74 extents.
        // Project the distinct vertices only.
        var vertices = ring.Count > 1 && SamePoint(ring[0], ring[^1])
            ? ring.Take(ring.Count - 1).ToList()
            : ring;
        var local = vertices.Select(point => GeometryMath.ToLocalMeters(point, reference)).ToList();

        var meanX = local.Average(point => point.X);
        var meanY = local.Average(point => point.Y);
        var varianceX = local.Average(point => (point.X - meanX) * (point.X - meanX));
        var varianceY = local.Average(point => (point.Y - meanY) * (point.Y - meanY));
        var covariance = local.Average(point => (point.X - meanX) * (point.Y - meanY));

        var angle = 0.5 * Math.Atan2(2 * covariance, varianceX - varianceY);
        var axisX = Math.Cos(angle);
        var axisY = Math.Sin(angle);

        var along = local.Select(point => (point.X * axisX) + (point.Y * axisY)).ToList();
        var across = local.Select(point => (-point.X * axisY) + (point.Y * axisX)).ToList();

        var length = along.Max() - along.Min();
        var width = across.Max() - across.Min();

        if (width > length)
        {
            // The formula's axis is the *first* principal component; when the perpendicular extent
            // is larger, that is the site's long axis, so rotate by ninety degrees and swap.
            (length, width) = (width, length);
            angle += Math.PI / 2.0;
            axisX = Math.Cos(angle);
            axisY = Math.Sin(angle);
        }

        if (length <= 0 || width / length >= NearSquareRatio)
        {
            // A near-square has two equally defensible axes; naming one would look like a measurement.
            return (length, width, null);
        }

        // A principal axis is a line, not a ray: report the bearing in [0, 180) so north-east and
        // south-west describe the same axis.
        var degrees = Math.Atan2(axisX, axisY) * 180.0 / Math.PI;
        var bearing = ((degrees % 180.0) + 180.0) % 180.0;

        return (length, width, bearing);
    }

    /// <summary>
    /// Attributes each boundary edge to the nearest named street within
    /// <see cref="FrontageToleranceMeters"/>, then sums per street. Unnamed roads are never counted:
    /// the figure must be checkable against a street the reader can name.
    /// </summary>
    private static List<StreetFrontage> Frontages(
        IReadOnlyList<GeoPoint> ring, IReadOnlyList<SiteGeometryStreet> streets)
    {
        var named = streets
            .Where(street => !string.IsNullOrWhiteSpace(street.Name) && street.Geometry.Count >= 2)
            .ToList();
        if (named.Count == 0 || ring.Count < 3)
        {
            return [];
        }

        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ring.Count; i++)
        {
            var from = ring[i];
            var to = ring[(i + 1) % ring.Count];
            var edgeLength = GeometryMath.DistanceMeters(from, to);
            if (edgeLength <= 0)
            {
                // A closed ring's repeated first point produces one zero-length edge.
                continue;
            }

            var nearest = named
                .Select(street => (Street: street, Distance: EdgeToStreetMeters(from, to, street.Geometry)))
                .OrderBy(candidate => candidate.Distance)
                .First();

            if (nearest.Distance > FrontageToleranceMeters)
            {
                continue;
            }

            var name = nearest.Street.Name!;
            totals[name] = totals.GetValueOrDefault(name) + edgeLength;
        }

        return
        [
            .. totals
                .OrderByDescending(entry => entry.Value)
                .Select(entry => new StreetFrontage(entry.Key, entry.Value)),
        ];
    }

    private static double EdgeToStreetMeters(GeoPoint from, GeoPoint to, IReadOnlyList<GeoPoint> street)
    {
        var distance = double.PositiveInfinity;
        foreach (var fraction in (double[])[0.25, 0.5, 0.75])
        {
            var sample = new GeoPoint(
                from.Latitude + ((to.Latitude - from.Latitude) * fraction),
                from.Longitude + ((to.Longitude - from.Longitude) * fraction));
            distance = Math.Min(distance, GeometryMath.DistanceToPathMeters(sample, street));
        }

        return distance;
    }

    private static bool SamePoint(GeoPoint a, GeoPoint b) =>
        Math.Abs(a.Latitude - b.Latitude) < 1e-9 && Math.Abs(a.Longitude - b.Longitude) < 1e-9;

    /// <summary>
    /// Recognises the platform's own manual-fallback boundary — a 24-vertex circle of constant
    /// radius (SPEC-042), used when no mapped boundary was found. Its figures are real measurements
    /// of that buffer, so the finding says what the shape is instead of implying a surveyed parcel.
    /// </summary>
    private static bool LooksLikeResolvedBuffer(IReadOnlyList<GeoPoint> ring)
    {
        if (ring.Count != BufferVertexCount)
        {
            return false;
        }

        var centre = GeometryMath.Centroid(ring);
        var distances = ring.Select(point => GeometryMath.DistanceMeters(centre, point)).ToList();
        var mean = distances.Average();

        return mean > 0 &&
               distances.All(distance => Math.Abs(distance - mean) / mean <= BufferRadiusTolerance);
    }
#pragma warning restore CA1822
}
