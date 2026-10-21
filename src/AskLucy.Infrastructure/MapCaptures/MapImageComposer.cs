using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.MapCaptures;

internal static partial class MapImageComposerLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Map capture image could not be decoded: {Reason}")]
    public static partial void DecodeFailed(ILogger logger, string reason);
}

/// <summary>
/// specs/083-viewer-map-capture T018 — the one Magick.NET pipeline both renderers share: decode,
/// paint footprint rings, crop 14 px from every edge, encode PNG, and enforce the size caps.
/// </summary>
/// <remarks>
/// Overlays are painted on the full provider frame and the crop is applied afterwards, so the
/// overlay geometry and the crop live in the same pixel space. The class holds no mutable state
/// beyond its options, so concurrent captures cannot interfere (FR-013).
/// </remarks>
public sealed class MapImageComposer(
    IOptions<MapCaptureOptions> options,
    ILogger<MapImageComposer> logger)
{
    /// <summary>Pixels removed from each edge of the provider frame.</summary>
    public const int CropPerEdge = 14;

    private const double StrokeWidth = 2;

    private static readonly MagickColor StrokeColor = MagickColors.Red;

    public MapImageResult Compose(
        byte[] providerBytes,
        GeoPoint center,
        int zoom,
        IReadOnlyList<BuildingFootprint> footprints)
    {
        try
        {
            using var image = new MagickImage(providerBytes);

            if (image.Width <= CropPerEdge * 2 || image.Height <= CropPerEdge * 2)
            {
                return MapImageResult.Failure(
                    MapCaptureFailureReason.CropTooSmall,
                    $"The provider image is {image.Width}x{image.Height} px, too small to crop {CropPerEdge} px from each edge.");
            }

            if (footprints.Count > 0)
            {
                DrawFootprints(image, center, zoom, footprints);
            }

            var width = (int)image.Width - CropPerEdge * 2;
            var height = (int)image.Height - CropPerEdge * 2;
            image.Crop(new MagickGeometry((uint)width, (uint)height)
            {
                X = CropPerEdge,
                Y = CropPerEdge,
            });

            var png = image.ToByteArray(MagickFormat.Png);

            var maxBytes = options.Value.MaxImageBytes;
            if (png.LongLength > maxBytes)
            {
                return MapImageResult.Failure(
                    MapCaptureFailureReason.ImageTooLarge,
                    $"The encoded image is {png.LongLength} bytes, over the {maxBytes} byte cap.");
            }

            return MapImageResult.Success(png, width, height);
        }
        catch (MagickException exception)
        {
            MapImageComposerLog.DecodeFailed(logger, exception.Message);
            return MapImageResult.Failure(
                MapCaptureFailureReason.ProviderUnavailable,
                "The imagery provider returned a file that could not be decoded.");
        }
    }

    private static void DrawFootprints(
        MagickImage image, GeoPoint center, int zoom, IReadOnlyList<BuildingFootprint> footprints)
    {
        var (west, south, east, north) = StaticMapFraming.CoveredBounds(center, zoom);
        var width = (double)image.Width;
        var height = (double)image.Height;

        double ToX(double longitude) => (longitude - west) / (east - west) * width;
        double ToY(double latitude) => (north - latitude) / (north - south) * height;

        var drawables = new Drawables();
        var drew = false;

        foreach (var footprint in footprints)
        {
            if (footprint.Ring.Count < 3)
            {
                continue;
            }

            var points = footprint.Ring
                .Select(point => new PointD(ToX(point.Longitude), ToY(point.Latitude)))
                .ToArray();

            drawables.FillColor(MagickColors.Transparent)
                .StrokeColor(StrokeColor)
                .StrokeWidth(StrokeWidth)
                .Polygon(points);

            drew = true;
        }

        if (drew)
        {
            drawables.Draw(image);
        }
    }
}
