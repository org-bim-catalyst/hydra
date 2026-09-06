using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// Turns any binary pixel mask into a vector ring: connected-component selection, exact
/// pixel-grid edge tracing, Douglas-Peucker simplification, then pixel-to-geo conversion. Shared
/// by two different mask producers (2026-09-06) — <see cref="RedOutlineVectorizer"/> (thresholding
/// a colour drawn by an image-generation model) and Gemini's native segmentation-mask output (a
/// genuine per-pixel classifier result, no image generation involved) — because once a producer
/// hands over "these pixels belong to the site," turning that into a ring is the same problem
/// regardless of where the pixels came from.
/// </summary>
/// <remarks>
/// Briefly included a morphological closing pass (dilate then erode) ahead of component selection,
/// to bridge label/marker holes and thin gaps. Removed once <c>maptype=terrain</c> turned out to
/// remove the actual cause (Google simply doesn't render building footprints on terrain tiles, at
/// any size) rather than needing a post-hoc gap-bridging fix — and closing had its own real cost:
/// a square structuring element chamfers any corner that isn't aligned with the pixel grid, which
/// silently rounded off every real corner of a site whose shape (like nearly every real property)
/// isn't grid-aligned in the source image. See docs/LOCATION_TO_BOUNDARY_END_TO_END.md §9.8.
/// </remarks>
internal static class MaskContourVectorizer
{
    /// <summary>Below this fraction of the image's own diagonal, a mask blob is treated as noise, not a boundary.</summary>
    private const double MinComponentDiagonalFraction = 0.15;

    /// <summary>Douglas-Peucker tolerance, in source pixels.</summary>
    private const double SimplifyEpsilonPixels = 3.0;

    public static IReadOnlyList<GeoPoint>? TryExtractRing(bool[,] mask, int width, int height, SatelliteImage bounds)
    {
        var pixelRing = TryExtractPixelRing(mask, width, height);
        return pixelRing is null ? null : ToGeoRing(pixelRing, width, height, bounds);
    }

    /// <summary>
    /// The pixel-space simplified ring, exposed separately so a caller that also wants to render
    /// the result (e.g. drawing it back onto the source image for a human to verify) works from the
    /// exact same coordinates used for the lat/lng conversion, rather than re-deriving pixel
    /// coordinates from the converted geo ring and risking a rounding mismatch between the two.
    /// </summary>
    internal static List<(int X, int Y)>? TryExtractPixelRing(bool[,] mask, int width, int height)
    {
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
        return simplified.Count < 3 ? null : simplified;
    }

    /// <summary>
    /// 8-connected flood fill over <paramref name="mask"/>, returning only the pixels of whichever
    /// component has the largest bounding-box diagonal — a boundary spans far more of the frame
    /// than any icon, label, or segmentation noise speck.
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
    /// one geometrically correct answer here. A mask with real thickness at its edge (or two
    /// disjoint blobs) can produce more than one loop; the one enclosing the largest area wins.
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

        var (i, j) = FarthestPointPairIndices(ring);

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

    internal static List<GeoPoint> ToGeoRing(List<(int X, int Y)> pixelRing, int width, int height, SatelliteImage bounds)
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
