using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;

namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/076 — the raster operations the height enrichment is built from: which cells a footprint
/// covers, connected regions of cells, and outlines traced around them. Outlines reuse the rendered
/// footprints' own tracer (<see cref="MaskContourVectorizer"/>), so a building outlined from
/// heights has the same shape rules as one outlined from Google's map.
/// </summary>
internal static class HeightMapRegions
{
    /// <summary>How far an outline may stray from the cell edges when simplified, in cells.</summary>
    private const double SimplifyToleranceCells = 0.75;

    /// <summary>
    /// The cells whose centres lie inside <paramref name="ring"/>. A footprint too small to contain
    /// a cell centre gets the cell under its centroid, so it can still be measured; one entirely off
    /// the grid gets none.
    /// </summary>
    public static List<(int X, int Y)> CellsUnder(GeoGrid grid, IReadOnlyList<GeoPoint> ring)
    {
        var cells = grid.CellsInside(ring);
        if (cells.Count > 0 || ring.Count == 0) return cells;

        var (x, y) = grid.ToGrid(ring.Average(p => p.Longitude), ring.Average(p => p.Latitude));
        var (cx, cy) = ((int)Math.Floor(x), (int)Math.Floor(y));
        if (cx >= 0 && cy >= 0 && cx < grid.Width && cy < grid.Height) cells.Add((cx, cy));
        return cells;
    }

    /// <summary>The median height over <paramref name="cells"/>, which must all be measured.</summary>
    public static double Median(BuildingHeightMap map, IEnumerable<(int X, int Y)> cells)
    {
        var values = cells.Select(c => map[c.X, c.Y]).ToArray();
        if (values.Length == 0) throw new ArgumentException("A median needs at least one cell.", nameof(cells));
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + (double)values[middle]) / 2;
    }

    /// <summary>
    /// The 4-connected regions of <paramref name="cells"/> with at least
    /// <paramref name="minimumCells"/> cells — 4-connected so two buildings touching only at a
    /// corner stay two buildings, as with rendered footprints.
    /// </summary>
    public static List<HashSet<(int X, int Y)>> Components(IEnumerable<(int X, int Y)> cells, int minimumCells)
    {
        var remaining = cells.ToHashSet();
        var components = new List<HashSet<(int X, int Y)>>();
        var queue = new Queue<(int X, int Y)>();
        while (remaining.Count > 0)
        {
            var start = remaining.First();
            remaining.Remove(start);
            var component = new HashSet<(int X, int Y)> { start };
            queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                foreach (var next in new[] { (cell.X + 1, cell.Y), (cell.X - 1, cell.Y), (cell.X, cell.Y + 1), (cell.X, cell.Y - 1) })
                {
                    if (!remaining.Remove(next)) continue;
                    component.Add(next);
                    queue.Enqueue(next);
                }
            }

            if (component.Count >= minimumCells) components.Add(component);
        }

        return components;
    }

    /// <summary>The closed outline around <paramref name="cells"/>, or <see langword="null"/> when it degenerates.</summary>
    public static IReadOnlyList<GeoPoint>? Outline(GeoGrid grid, HashSet<(int X, int Y)> cells)
    {
        var traced = MaskContourVectorizer.TraceOuterRing(cells, grid.Width, grid.Height);
        if (traced is null || traced.Count < 3) return null;

        var simplified = MaskContourVectorizer.DouglasPeucker(traced, SimplifyToleranceCells);
        if (simplified.Count < 3) return null;

        var ring = simplified.Select(p => grid.FromGrid(p.X, p.Y)).ToList();
        if (ring[0] != ring[^1]) ring.Add(ring[0]);
        return ring;
    }

    /// <summary>Grows <paramref name="mask"/> by <paramref name="cells"/> in every direction, diagonals included.</summary>
    public static bool[,] Dilate(bool[,] mask, int cells) => Morph(mask, cells, grow: true);

    /// <summary>
    /// Morphological opening with a 3 × 3 cell: removes slivers under three cells wide while
    /// keeping the shape of anything larger — the ragged edge a building's misaligned neighbour
    /// leaves behind, not a building.
    /// </summary>
    public static bool[,] Open(bool[,] mask) => Morph(Morph(mask, 1, grow: false), 1, grow: true);

    /// <summary>
    /// Dilation (<paramref name="grow"/>) or erosion by a square of side 2 × <paramref name="cells"/> + 1,
    /// done as a row pass then a column pass since a square is separable. Cells beyond the edge
    /// count as empty.
    /// </summary>
    private static bool[,] Morph(bool[,] mask, int cells, bool grow)
    {
        var (width, height) = (mask.GetLength(0), mask.GetLength(1));
        var rows = new bool[width, height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                rows[x, y] = Window(i => mask[i, y], x, width, cells, grow);
            }
        }

        var result = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                result[x, y] = Window(i => rows[x, i], y, height, cells, grow);
            }
        }

        return result;
    }

    private static bool Window(Func<int, bool> at, int centre, int length, int cells, bool grow)
    {
        for (var i = centre - cells; i <= centre + cells; i++)
        {
            var set = i >= 0 && i < length && at(i);
            if (grow && set) return true;
            if (!grow && !set) return false;
        }

        return !grow;
    }
}
