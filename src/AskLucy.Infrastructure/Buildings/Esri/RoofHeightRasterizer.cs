using AskLucy.Application.Buildings;

namespace AskLucy.Infrastructure.Buildings.Esri;

/// <summary>
/// specs/076 — draws an I3S node's roofs, seen from above, into a height grid. Each feature's
/// ground is its top minus its height attribute: the mesh's own elevations are relative to the
/// node and its walls run a few metres below ground, so neither the lowest vertex nor zero is the
/// ground. Every cell keeps the tallest roof over its centre.
/// </summary>
internal static class RoofHeightRasterizer
{
    /// <summary>Below this a cell is ground-level mesh (walls sunk into the terrain, plinths), not a roof.</summary>
    internal const float MinimumRoofHeightMetres = 1.0f;

    /// <param name="featureHeights">The height attribute, indexed by feature.</param>
    /// <param name="include">Which features to draw — measured ones only.</param>
    public static void Rasterize(I3sMesh mesh, double[] featureHeights, bool[] include, GeoGrid grid, float[] heights)
    {
        var top = new float[featureHeights.Length];
        Array.Fill(top, float.MinValue);
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            var f = mesh.Features[i];
            if ((uint)f < (uint)top.Length) top[f] = Math.Max(top[f], mesh.Elevations[i]);
        }

        var x = new double[mesh.VertexCount];
        var y = new double[mesh.VertexCount];
        for (var i = 0; i < mesh.VertexCount; i++)
        {
            (x[i], y[i]) = grid.ToGrid(mesh.Longitudes[i], mesh.Latitudes[i]);
        }

        for (var t = 0; t + 2 < mesh.Triangles.Length; t += 3)
        {
            int a = mesh.Triangles[t], b = mesh.Triangles[t + 1], c = mesh.Triangles[t + 2];
            var f = mesh.Features[a];
            if ((uint)f >= (uint)include.Length || !include[f]) continue;

            var ground = top[f] - (float)featureHeights[f];
            RaiseTriangle(heights, grid,
                (x[a], y[a], mesh.Elevations[a] - ground),
                (x[b], y[b], mesh.Elevations[b] - ground),
                (x[c], y[c], mesh.Elevations[c] - ground));
        }
    }

    /// <summary>
    /// Raises every cell whose centre the triangle covers to the triangle's interpolated height
    /// there. Vertices are in cell units (see <see cref="GeoGrid.ToGrid"/>). A wall seen from above
    /// has no area and covers nothing.
    /// </summary>
    internal static void RaiseTriangle(float[] heights, GeoGrid grid, (double X, double Y, float Z) a, (double X, double Y, float Z) b, (double X, double Y, float Z) c)
    {
        var area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (Math.Abs(area) < 1e-9) return;

        var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)) - 0.5));
        var maxX = Math.Min(grid.Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)) - 0.5));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 0.5));
        var maxY = Math.Min(grid.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - 0.5));

        // Shared edges between a roof's triangles must not leave unfilled centres between them.
        const double Tolerance = -1e-9;
        for (var cy = minY; cy <= maxY; cy++)
        {
            var py = cy + 0.5;
            for (var cx = minX; cx <= maxX; cx++)
            {
                var px = cx + 0.5;
                var w0 = (((b.X - px) * (c.Y - py)) - ((c.X - px) * (b.Y - py))) / area;
                var w1 = (((c.X - px) * (a.Y - py)) - ((a.X - px) * (c.Y - py))) / area;
                var w2 = 1 - w0 - w1;
                if (w0 < Tolerance || w1 < Tolerance || w2 < Tolerance) continue;

                var z = (float)((w0 * a.Z) + (w1 * b.Z) + (w2 * c.Z));
                if (z < MinimumRoofHeightMetres) continue;

                var index = (cy * grid.Width) + cx;
                if (float.IsNaN(heights[index]) || z > heights[index]) heights[index] = z;
            }
        }
    }
}
