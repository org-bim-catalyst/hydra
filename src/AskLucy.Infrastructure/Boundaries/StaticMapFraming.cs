using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// The zoom-to-fit and Web Mercator bounds math shared by every Google Static Maps fetch in this
/// module — originally private to <see cref="GoogleSatelliteImageProvider"/>, extracted (2026-09-06)
/// once <see cref="GoogleRenderedFillBoundaryExtractor"/> needed the exact same framing for a
/// differently-styled request against the same endpoint. Both callers need identical math: the
/// bounds returned here are what pixel coordinates get interpolated back into latitude/longitude
/// against, so any drift between two independently-written copies would silently corrupt one of
/// them.
/// </summary>
internal static class StaticMapFraming
{
    /// <summary>Ground coverage of the request, in Static Maps' scale-1 pixels. <c>scale=2</c> returns twice this many pixels for the same ground.</summary>
    public const int ImageSizePixels = 640;

    /// <summary>Static Maps rejects a larger zoom than this.</summary>
    public const int MaxZoom = 20;

    /// <summary>Below this the image is too coarse to be worth analysing at all.</summary>
    private const int MinZoom = 14;

    private const double EarthCircumferenceMeters = 40_075_016.686;

    /// <summary>
    /// The largest zoom whose frame still contains <paramref name="radiusMeters"/> in every
    /// direction — "zoom to fit", so the site fills as much of the image as it can without being
    /// clipped.
    /// </summary>
    /// <remarks>
    /// Static Maps only accepts integer zoom, so the chosen level almost always covers somewhat
    /// more ground than asked for. Erring outwards is deliberate: a clipped site loses the very
    /// corners any downstream reader is meant to see.
    /// </remarks>
    public static int ChooseZoomToFit(double latitude, int radiusMeters)
    {
        var requiredMetersPerPixel = Math.Max(radiusMeters, 1) * 2.0 / ImageSizePixels;
        var scale = EarthCircumferenceMeters * Math.Abs(Math.Cos(latitude * Math.PI / 180.0)) / 256.0;
        var zoom = (int)Math.Floor(Math.Log2(scale / requiredMetersPerPixel));
        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public static double MetersPerPixel(double latitude, int zoom) =>
        EarthCircumferenceMeters * Math.Abs(Math.Cos(latitude * Math.PI / 180.0)) / (256.0 * Math.Pow(2, zoom));

    /// <summary>
    /// The exact ground rectangle the returned image covers, in Web Mercator — the projection
    /// Static Maps renders in.
    /// </summary>
    /// <remarks>
    /// Computed rather than assumed, because a downstream reader maps normalised [0,1] (or pixel)
    /// coordinates back to latitude/longitude by interpolating linearly between these edges. Over a
    /// few hundred metres Mercator's latitude curvature costs well under a metre; over a frame sized
    /// from a guess, being wrong about the edges costs the whole result.
    /// </remarks>
    public static (double West, double South, double East, double North) CoveredBounds(GeoPoint center, int zoom)
    {
        var worldSize = 256.0 * Math.Pow(2, zoom);
        var half = ImageSizePixels / 2.0;

        var centerX = (center.Longitude + 180.0) / 360.0 * worldSize;
        var centerY = MercatorY(center.Latitude) * worldSize;

        var west = ((centerX - half) / worldSize * 360.0) - 180.0;
        var east = ((centerX + half) / worldSize * 360.0) - 180.0;
        var north = InverseMercatorY((centerY - half) / worldSize);
        var south = InverseMercatorY((centerY + half) / worldSize);

        return (west, south, east, north);
    }

    /// <summary>
    /// The point <paramref name="dxPixels"/>/<paramref name="dyPixels"/> scale-1 pixels away from
    /// <paramref name="center"/> at <paramref name="zoom"/>, in the exact same Web Mercator pixel
    /// space <see cref="CoveredBounds"/> already uses. Exists for tiling: a caller fetching several
    /// adjacent Static Maps frames to stitch into one larger, higher-resolution image needs their
    /// centres to land exactly edge-to-edge with zero gap or overlap, which only pixel-space offsets
    /// (not a separate lat/lng-metres approximation) guarantee — confirmed live: four tiles offset
    /// by ±<see cref="ImageSizePixels"/>/2 this way produced a stitched frame whose overall bounds
    /// matched a single lower-zoom tile's bounds to full floating-point precision, no seam.
    /// </summary>
    public static GeoPoint OffsetByPixels(GeoPoint center, int zoom, double dxPixels, double dyPixels)
    {
        var worldSize = 256.0 * Math.Pow(2, zoom);
        var centerX = (center.Longitude + 180.0) / 360.0 * worldSize;
        var centerY = MercatorY(center.Latitude) * worldSize;

        var newX = centerX + dxPixels;
        var newY = centerY + dyPixels;

        var newLongitude = (newX / worldSize * 360.0) - 180.0;
        var newLatitude = InverseMercatorY(newY / worldSize);
        return new GeoPoint(newLatitude, newLongitude);
    }

    private static double MercatorY(double latitude)
    {
        var sin = Math.Sin(latitude * Math.PI / 180.0);
        return 0.5 - (Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI));
    }

    private static double InverseMercatorY(double y)
    {
        var n = Math.PI - (2.0 * Math.PI * y);
        return 180.0 / Math.PI * Math.Atan(0.5 * (Math.Exp(n) - Math.Exp(-n)));
    }
}
