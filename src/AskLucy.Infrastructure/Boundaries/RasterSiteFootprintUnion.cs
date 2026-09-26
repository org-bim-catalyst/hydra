using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// specs/077 — joins footprints by drawing them onto a fine grid, closing gaps narrower than the
/// bridge distance, and tracing the result back into outlines with the same
/// <see cref="MaskContourVectorizer"/> the rendered-footprint path uses.
/// <para>
/// Raster rather than a vector polygon union because the inputs are not clean: OSM draws
/// BurJuman's hotel 0.5 m off the mall, so a vector union keeps two outlines with a sliver between
/// them, and snapping that sliver shut is exactly what a morphological closing does. The cost is
/// half-metre vertex precision, well inside what the source mapping itself offers, and no new
/// geometry dependency.
/// </para>
/// </summary>
internal sealed class RasterSiteFootprintUnion : ISiteFootprintUnion
{
    private const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>Grid resolution, coarsened only when the site would otherwise exceed <see cref="MaximumGridPixels"/> on a side.</summary>
    private const double PreferredMetersPerPixel = 0.5;

    private const int MaximumGridPixels = 2000;

    /// <summary>Simplification tolerance, in pixels — removes the staircase the grid leaves on diagonal walls.</summary>
    private const double SimplifyEpsilonPixels = 1.0;

    public IReadOnlyList<IReadOnlyList<GeoPoint>> Union(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, double bridgeGapMeters)
    {
        var usable = rings.Where(r => r.Count >= 3).ToList();
        if (usable.Count == 0)
        {
            return [];
        }

        var points = usable.SelectMany(r => r).ToList();
        var south = points.Min(p => p.Latitude);
        var north = points.Max(p => p.Latitude);
        var west = points.Min(p => p.Longitude);
        var east = points.Max(p => p.Longitude);

        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos((south + north) / 2 * Math.PI / 180.0);
        var spanMeters = Math.Max((north - south) * MetersPerDegreeLatitude, (east - west) * metersPerDegreeLongitude);
        var metersPerPixel = Math.Max(PreferredMetersPerPixel, spanMeters / (MaximumGridPixels - 64));

        var radius = (int)Math.Ceiling(Math.Max(0, bridgeGapMeters) / 2 / metersPerPixel);
        var pad = radius + 2;
        var degreesPerPixelLatitude = metersPerPixel / MetersPerDegreeLatitude;
        var degreesPerPixelLongitude = metersPerPixel / metersPerDegreeLongitude;

        var width = (int)Math.Ceiling((east - west) / degreesPerPixelLongitude) + (2 * pad);
        var height = (int)Math.Ceiling((north - south) / degreesPerPixelLatitude) + (2 * pad);
        var bounds = new SatelliteImage(
            [], string.Empty,
            West: west - (pad * degreesPerPixelLongitude),
            South: north + (pad * degreesPerPixelLatitude) - (height * degreesPerPixelLatitude),
            East: west - (pad * degreesPerPixelLongitude) + (width * degreesPerPixelLongitude),
            North: north + (pad * degreesPerPixelLatitude));

        var mask = new bool[width, height];
        foreach (var ring in usable)
        {
            Fill(mask, width, height,
                [.. ring.Select(p => ((p.Longitude - bounds.West) / degreesPerPixelLongitude, (bounds.North - p.Latitude) / degreesPerPixelLatitude))]);
        }

        if (radius > 0)
        {
            mask = Erode(Dilate(mask, width, height, radius), width, height, radius);
        }

        var traced = MaskContourVectorizer.ExtractAllRings(
            mask, width, height, bounds, minimumPixelArea: 4, SimplifyEpsilonPixels, eightConnected: false);

        return [.. traced.Rings
            .Select(r => r.Geo)
            .OrderByDescending(GeometryMath.AreaSquareMeters)];
    }

    /// <summary>Even-odd scanline fill, sampling each pixel at its centre.</summary>
    private static void Fill(bool[,] mask, int width, int height, IReadOnlyList<(double X, double Y)> ring)
    {
        var crossings = new List<double>();
        for (var y = 0; y < height; y++)
        {
            var sampleY = y + 0.5;
            crossings.Clear();
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (xi, yi) = ring[i];
                var (xj, yj) = ring[j];
                if ((yi > sampleY) != (yj > sampleY))
                {
                    crossings.Add(xi + ((sampleY - yi) / (yj - yi) * (xj - xi)));
                }
            }

            crossings.Sort();
            for (var k = 0; k + 1 < crossings.Count; k += 2)
            {
                var from = Math.Max(0, (int)Math.Ceiling(crossings[k] - 0.5));
                var to = Math.Min(width - 1, (int)Math.Floor(crossings[k + 1] - 0.5));
                for (var x = from; x <= to; x++)
                {
                    mask[x, y] = true;
                }
            }
        }
    }

    /// <summary>Square-element dilation, done as two one-dimensional passes.</summary>
    private static bool[,] Dilate(bool[,] mask, int width, int height, int radius) =>
        Pass(Pass(mask, width, height, radius, horizontal: true, any: true), width, height, radius, horizontal: false, any: true);

    private static bool[,] Erode(bool[,] mask, int width, int height, int radius) =>
        Pass(Pass(mask, width, height, radius, horizontal: true, any: false), width, height, radius, horizontal: false, any: false);

    /// <summary>
    /// One axis of a square structuring element: a pixel is set when any (dilation) or every
    /// (erosion) pixel within <paramref name="radius"/> along the axis is. Out-of-grid counts as
    /// unset, which the padding keeps away from every footprint.
    /// </summary>
    private static bool[,] Pass(bool[,] mask, int width, int height, int radius, bool horizontal, bool any)
    {
        var result = new bool[width, height];
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                var value = !any;
                for (var d = -radius; d <= radius; d++)
                {
                    var sx = horizontal ? x + d : x;
                    var sy = horizontal ? y : y + d;
                    var set = sx >= 0 && sx < width && sy >= 0 && sy < height && mask[sx, sy];
                    if (any && set)
                    {
                        value = true;
                        break;
                    }

                    if (!any && !set)
                    {
                        value = false;
                        break;
                    }
                }

                result[x, y] = value;
            }
        }

        return result;
    }
}
