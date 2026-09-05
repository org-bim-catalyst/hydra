using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// Turns a bright-red outline drawn by an image-generation model back into a vector ring — the
/// deterministic half of the "draw, don't describe" diagnostic pivot (2026-09-06). Asking a model
/// to draw directly on an image, its actual strength, proved far more accurate than asking it to
/// report normalised pixel coordinates for the same boundary (three separate coordinate-prompt
/// fixes never moved a real, user-confirmed notch; the drawn image captured it on the first try).
/// This class does the "coordinates" half deterministically instead of asking a model for them a
/// second time, since that is the exact task type that kept failing.
/// </summary>
/// <remarks>
/// <para>
/// Extraction is entirely pixel-based: find red pixels, keep only the largest connected blob (a
/// hospital-pin icon or red label text elsewhere on the map is small; a boundary outline spans
/// most of the frame), trace the outer edge of that blob's pixel grid exactly (not an
/// approximation — every foreground/background cell boundary is walked), then simplify the
/// resulting staircase into a small vertex list with Douglas-Peucker. A genuinely notched or
/// curved outline keeps its extra vertices through simplification; a straight edge collapses to
/// two endpoints.
/// </para>
/// <para>
/// Never throws (constitution §VIII) and returns <see langword="null"/> — never a best-effort
/// guess — whenever the image doesn't contain a plausible outline: too little red, a shape too
/// small to be a boundary, or edge topology that fails to close into one simple loop. The caller
/// (<see cref="GeminiBoundaryDrawDiagnosticService"/>) treats a null result as "try the fallback
/// second AI call" rather than a hard failure.
/// </para>
/// </remarks>
internal static class RedOutlineVectorizer
{
    /// <summary>Loosely tuned against a "bright red" marker line; not sampled from a real Nano Banana output yet, so a miss here simply falls through to the AI-read fallback rather than mis-vectorizing.</summary>
    private const byte MinRed = 170;
    private const byte MaxGreenOrBlue = 100;
    private const int MinRedDominance = 60;

    /// <summary>Below this fraction of the image's own diagonal, a red blob is treated as an icon or label, not a boundary.</summary>
    private const double MinComponentDiagonalFraction = 0.15;

    /// <summary>Douglas-Peucker tolerance, in source pixels — small enough to keep a genuine multi-metre notch, large enough to erase single-pixel staircase jitter.</summary>
    private const double SimplifyEpsilonPixels = 3.0;

    public static IReadOnlyList<GeoPoint>? TryExtractRing(byte[] imageBytes, SatelliteImage bounds)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        var width = image.Width;
        var height = image.Height;

        var mask = new bool[width, height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    mask[x, y] = IsBoundaryRed(row[x]);
                }
            }
        });

        var component = LargestComponent(mask, width, height);
        if (component is null)
        {
            return null;
        }

        var ring = TraceOuterRing(component, width, height);
        if (ring is null || ring.Count < 3)
        {
            return null;
        }

        var simplified = DouglasPeucker(ring, SimplifyEpsilonPixels);
        if (simplified.Count < 3)
        {
            return null;
        }

        return ToGeoRing(simplified, width, height, bounds);
    }

    private static bool IsBoundaryRed(Rgba32 pixel) =>
        pixel.R >= MinRed
        && pixel.G <= MaxGreenOrBlue
        && pixel.B <= MaxGreenOrBlue
        && pixel.R - pixel.G >= MinRedDominance
        && pixel.R - pixel.B >= MinRedDominance;

    /// <summary>
    /// 8-connected flood fill over <paramref name="mask"/>, returning only the pixels of whichever
    /// component has the largest bounding-box diagonal — a boundary outline spans far more of the
    /// frame than any icon or text label drawn in the same colour.
    /// </summary>
    internal static HashSet<(int X, int Y)>? LargestComponent(bool[,] mask, int width, int height)
    {
        var visited = new bool[width, height];
        HashSet<(int X, int Y)>? largest = null;
        var largestDiagonalSquared = 0.0;

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if (!mask[x, y] || visited[x, y])
                {
                    continue;
                }

                var component = new HashSet<(int X, int Y)>();
                var queue = new Queue<(int X, int Y)>();
                queue.Enqueue((x, y));
                visited[x, y] = true;

                var minX = x; var maxX = x; var minY = y; var maxY = y;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    component.Add((cx, cy));
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                    minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            var nx = cx + dx; var ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            if (!mask[nx, ny] || visited[nx, ny]) continue;
                            visited[nx, ny] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }

                var diagonalSquared = ((double)(maxX - minX) * (maxX - minX)) + ((double)(maxY - minY) * (maxY - minY));
                if (diagonalSquared > largestDiagonalSquared)
                {
                    largestDiagonalSquared = diagonalSquared;
                    largest = component;
                }
            }
        }

        var imageDiagonal = Math.Sqrt(((double)width * width) + ((double)height * height));
        var minDiagonal = imageDiagonal * MinComponentDiagonalFraction;
        return largestDiagonalSquared >= minDiagonal * minDiagonal ? largest : null;
    }

    /// <summary>
    /// Walks the exact outer edge of <paramref name="cells"/>'s pixel-grid footprint. Every
    /// foreground cell contributes a directed unit edge for each side that borders a background (or
    /// out-of-bounds) cell, oriented consistently clockwise around the shape; edges between two
    /// foreground cells are never added. Chaining these tail-to-head yields one or more simple
    /// closed loops with no ambiguity about direction — unlike pixel-centre tracing, there is only
    /// one geometrically correct answer here. A stroke with real thickness produces two loops (its
    /// outer and inner edge); the one enclosing the larger area is the outer boundary this returns.
    /// </summary>
    internal static List<(int X, int Y)>? TraceOuterRing(HashSet<(int X, int Y)> cells, int width, int height)
    {
        var next = new Dictionary<(int X, int Y), (int X, int Y)>();

        void AddEdge((int X, int Y) from, (int X, int Y) to) => next[from] = to;

        bool IsForeground(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && cells.Contains((x, y));

        foreach (var (x, y) in cells)
        {
            // Corners of this cell, clockwise: top-left, top-right, bottom-right, bottom-left.
            var tl = (x, y);
            var tr = (x + 1, y);
            var br = (x + 1, y + 1);
            var bl = (x, y + 1);

            if (!IsForeground(x, y - 1)) AddEdge(tl, tr);       // top
            if (!IsForeground(x + 1, y)) AddEdge(tr, br);       // right
            if (!IsForeground(x, y + 1)) AddEdge(br, bl);       // bottom
            if (!IsForeground(x - 1, y)) AddEdge(bl, tl);       // left
        }

        if (next.Count == 0)
        {
            return null;
        }

        var visitedStarts = new HashSet<(int X, int Y)>();
        List<(int X, int Y)>? bestLoop = null;
        var bestArea = 0.0;

        foreach (var start in next.Keys)
        {
            if (visitedStarts.Contains(start))
            {
                continue;
            }

            var loop = new List<(int X, int Y)> { start };
            var current = start;
            var guard = 0;
            var maxSteps = next.Count + 1;

            while (guard++ < maxSteps)
            {
                visitedStarts.Add(current);
                if (!next.TryGetValue(current, out var nextPoint))
                {
                    loop = null;
                    break;
                }

                if (nextPoint == start)
                {
                    break;
                }

                loop.Add(nextPoint);
                current = nextPoint;
            }

            if (loop is null || loop.Count < 3)
            {
                continue;
            }

            var area = Math.Abs(ShoelaceArea(loop));
            if (area > bestArea)
            {
                bestArea = area;
                bestLoop = loop;
            }
        }

        return bestLoop;
    }

    private static double ShoelaceArea(List<(int X, int Y)> ring)
    {
        double sum = 0;
        for (var i = 0; i < ring.Count; i++)
        {
            var (x1, y1) = ring[i];
            var (x2, y2) = ring[(i + 1) % ring.Count];
            sum += ((double)x1 * y2) - ((double)x2 * y1);
        }
        return sum / 2.0;
    }

    /// <summary>Standard recursive Douglas-Peucker over a closed ring: fixes the two farthest-apart points as anchors, then simplifies each half independently.</summary>
    internal static List<(int X, int Y)> DouglasPeucker(List<(int X, int Y)> ring, double epsilon)
    {
        if (ring.Count < 4)
        {
            return ring;
        }

        var farthestSplit = FarthestPointPairIndices(ring);
        var (i, j) = farthestSplit;

        var firstHalf = SimplifyOpen(SliceLoop(ring, i, j), epsilon);
        var secondHalf = SimplifyOpen(SliceLoop(ring, j, i), epsilon);

        // secondHalf's last point (== firstHalf's first point) is dropped to avoid duplicating the seam.
        var result = new List<(int X, int Y)>(firstHalf);
        result.AddRange(secondHalf.Skip(1).Take(secondHalf.Count - 2));
        return result;
    }

    private static (int, int) FarthestPointPairIndices(List<(int X, int Y)> ring)
    {
        var bestI = 0;
        var bestJ = ring.Count / 2;
        var bestDistSquared = -1.0;

        for (var i = 0; i < ring.Count; i++)
        {
            for (var j = i + 1; j < ring.Count; j++)
            {
                var dx = ring[i].X - ring[j].X;
                var dy = ring[i].Y - ring[j].Y;
                var distSquared = ((double)dx * dx) + ((double)dy * dy);
                if (distSquared > bestDistSquared)
                {
                    bestDistSquared = distSquared;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        return (bestI, bestJ);
    }

    private static List<(int X, int Y)> SliceLoop(List<(int X, int Y)> ring, int from, int to)
    {
        var slice = new List<(int X, int Y)>();
        var idx = from;
        while (true)
        {
            slice.Add(ring[idx]);
            if (idx == to) break;
            idx = (idx + 1) % ring.Count;
        }
        return slice;
    }

    /// <summary>Classic open-polyline Douglas-Peucker, first and last points always kept.</summary>
    private static List<(int X, int Y)> SimplifyOpen(List<(int X, int Y)> points, double epsilon)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var first = points[0];
        var last = points[^1];
        var maxDist = 0.0;
        var maxIndex = 0;

        for (var i = 1; i < points.Count - 1; i++)
        {
            var dist = PerpendicularDistance(points[i], first, last);
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist <= epsilon)
        {
            return [first, last];
        }

        var left = SimplifyOpen(points[..(maxIndex + 1)], epsilon);
        var right = SimplifyOpen(points[maxIndex..], epsilon);

        var combined = new List<(int X, int Y)>(left);
        combined.AddRange(right.Skip(1));
        return combined;
    }

    private static double PerpendicularDistance((int X, int Y) point, (int X, int Y) lineStart, (int X, int Y) lineEnd)
    {
        double dx = lineEnd.X - lineStart.X;
        double dy = lineEnd.Y - lineStart.Y;
        if (dx == 0 && dy == 0)
        {
            dx = point.X - lineStart.X;
            dy = point.Y - lineStart.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        var t = (((point.X - lineStart.X) * dx) + ((point.Y - lineStart.Y) * dy)) / ((dx * dx) + (dy * dy));
        var projX = lineStart.X + (t * dx);
        var projY = lineStart.Y + (t * dy);
        var distX = point.X - projX;
        var distY = point.Y - projY;
        return Math.Sqrt((distX * distX) + (distY * distY));
    }

    private static List<GeoPoint> ToGeoRing(List<(int X, int Y)> pixelRing, int width, int height, SatelliteImage bounds)
    {
        var geo = new List<GeoPoint>(pixelRing.Count + 1);
        foreach (var (x, y) in pixelRing)
        {
            var fracX = (double)x / width;
            var fracY = (double)y / height;
            var longitude = bounds.West + (fracX * (bounds.East - bounds.West));
            var latitude = bounds.North - (fracY * (bounds.North - bounds.South));
            geo.Add(new GeoPoint(latitude, longitude));
        }

        if (geo.Count > 0 && (geo[0].Latitude != geo[^1].Latitude || geo[0].Longitude != geo[^1].Longitude))
        {
            geo.Add(geo[0]);
        }

        return geo;
    }
}
