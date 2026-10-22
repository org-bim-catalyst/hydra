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
/// paint footprint rings, crop the requested number of pixels from every edge, encode PNG, and
/// enforce the size caps.
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
    /// <summary>
    /// Google's Static Maps satellite layer is tone-mapped for a browser: measured against Esri's
    /// analysis-grade render of the very same frame, Google's blacks sit at about 52% instead of
    /// about 7%, its contrast is roughly a third of the truth, and its colour is about half
    /// saturated. These two operations reverse that — a level that puts the black point back
    /// where the photography has it, and a saturation that restores what the tone curve took.
    /// </summary>
    /// <remarks>
    /// The numbers were fitted by minimising the difference between the corrected image and
    /// Esri's render of the identical frame — the same photograph, the same rectangle, the same
    /// pixels — so the target is a known-good rendering of the same scene, not a guess.
    /// </remarks>
    /// <remarks>
    /// It is a correction, not a repair: anything baked into the pixels — Google's logo, its
    /// attribution line, an aircraft that happened to be passing — is still there. The crop is
    /// what removes the provider's own branding.
    /// </remarks>
    private static readonly Percentage ToneMappedBlackPoint = new(45);
    private static readonly Percentage ToneMappedWhitePoint = new(99);
    private const double ToneMappedGamma = 1.1;
    private static readonly Percentage ToneMappedSaturation = new(120);

    public MapImageResult Compose(
        byte[] providerBytes,
        GeoPoint center,
        int zoom,
        IReadOnlyList<BuildingFootprint> footprints,
        int cropPerEdge = 32,
        bool enhanceColour = false)
    {
        try
        {
            using var image = new MagickImage(providerBytes);

            if (image.Width <= cropPerEdge * 2 || image.Height <= cropPerEdge * 2)
            {
                return MapImageResult.Failure(
                    MapCaptureFailureReason.CropTooSmall,
                    $"The provider image is {image.Width}x{image.Height} px, too small to crop {cropPerEdge} px from each edge.");
            }

            if (footprints.Count > 0)
            {
                DrawFootprints(image, center, zoom, footprints);
            }

            if (enhanceColour)
            {
                RestoreToneMappedColour(image);
            }

            var width = (int)image.Width - cropPerEdge * 2;
            var height = (int)image.Height - cropPerEdge * 2;
            if (cropPerEdge > 0)
            {
                image.Crop(new MagickGeometry((uint)width, (uint)height)
                {
                    X = cropPerEdge,
                    Y = cropPerEdge,
                });
            }

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

    /// <summary>
    /// Undoes a provider's web tone mapping: the black point first (which is what the tone curve
    /// lifted), then the saturation (which it drained). Order matters — boosting the saturation of
    /// a lifted image amplifies the lifted blacks with it.
    /// </summary>
    private static void RestoreToneMappedColour(MagickImage image)
    {
        image.Level(ToneMappedBlackPoint, ToneMappedWhitePoint, ToneMappedGamma, Channels.All);
        image.Modulate(new Percentage(100), ToneMappedSaturation, new Percentage(100));
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

    private static readonly MagickColor StrokeColor = MagickColors.Red;

    private const double StrokeWidth = 2;
}
