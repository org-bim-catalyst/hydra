using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Buildings;

/// <summary>
/// specs/076 — a north-up grid of square-ish cells over a small area, aligned to latitude and
/// longitude. Row 0 is the northern edge, like an image. At site scale (a few hundred metres) the
/// linear lat/lon mapping is accurate to centimetres, which is why no projection library is used.
/// </summary>
public sealed class GeoGrid
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    public GeoGrid(double west, double south, double east, double north, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A grid needs at least one cell.");
        if (east <= west || north <= south) throw new ArgumentException("A grid's bounds must be non-empty.");
        (West, South, East, North, Width, Height) = (west, south, east, north, width, height);
    }

    public double West { get; }

    public double South { get; }

    public double East { get; }

    public double North { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The north–south size of one cell, in metres.</summary>
    public double CellMetres => (North - South) / Height * MetersPerDegreeLatitude;

    /// <summary>A grid of <paramref name="cellMetres"/> cells covering a square of side 2 × <paramref name="halfExtentMetres"/>.</summary>
    public static GeoGrid Around(GeoPoint center, double halfExtentMetres, double cellMetres)
    {
        var cells = Math.Max(1, (int)Math.Ceiling(2 * halfExtentMetres / cellMetres));
        var halfLatitude = cells * cellMetres / 2 / MetersPerDegreeLatitude;
        var halfLongitude = halfLatitude / Math.Cos(center.Latitude * Math.PI / 180);
        return new GeoGrid(
            center.Longitude - halfLongitude, center.Latitude - halfLatitude,
            center.Longitude + halfLongitude, center.Latitude + halfLatitude,
            cells, cells);
    }

    /// <summary>A point's position in cell units: x grows east, y grows south.</summary>
    public (double X, double Y) ToGrid(double longitude, double latitude) =>
        ((longitude - West) / (East - West) * Width, (North - latitude) / (North - South) * Height);

    /// <summary>The inverse of <see cref="ToGrid"/>: the point at a position in cell units.</summary>
    public GeoPoint FromGrid(double x, double y) => new(
        North - (y / Height * (North - South)),
        West + (x / Width * (East - West)));

    public GeoPoint CellCentre(int x, int y) => FromGrid(x + 0.5, y + 0.5);

    /// <summary>
    /// Every cell whose centre lies inside <paramref name="ring"/> (even–odd rule), row by row. A
    /// ring smaller than a cell can contain no centre; callers that need an answer for it sample
    /// the ring's centroid instead.
    /// </summary>
    public List<(int X, int Y)> CellsInside(IReadOnlyList<GeoPoint> ring)
    {
        var cells = new List<(int X, int Y)>();
        if (ring.Count < 3) return cells;

        var points = ring.Select(p => ToGrid(p.Longitude, p.Latitude)).ToArray();
        var minY = Math.Max(0, (int)Math.Floor(points.Min(p => p.Y)));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(points.Max(p => p.Y)));
        var crossings = new List<double>();
        for (var y = minY; y <= maxY; y++)
        {
            var rowCentre = y + 0.5;
            crossings.Clear();
            for (var i = 0; i < points.Length; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                if ((a.Y <= rowCentre) == (b.Y <= rowCentre)) continue;
                crossings.Add(a.X + ((rowCentre - a.Y) / (b.Y - a.Y) * (b.X - a.X)));
            }

            crossings.Sort();
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                // Cell x's centre is x + 0.5, so it is inside when left <= x + 0.5 < right.
                var first = Math.Max(0, (int)Math.Ceiling(crossings[i] - 0.5));
                var last = Math.Min(Width - 1, (int)Math.Ceiling(crossings[i + 1] - 0.5) - 1);
                for (var x = first; x <= last; x++) cells.Add((x, y));
            }
        }

        return cells;
    }
}
