using AskLucy.Application.Buildings;
using AskLucy.Application.MapCaptures;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.MapCaptures;
using FluentAssertions;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AskLucy.Infrastructure.Tests.MapCaptures;

/// <summary>
/// specs/083-viewer-map-capture T012 — the per-edge crop, the 10 MB cap, overlay pixel
/// mapping, the colour correction, and statelessness under concurrency (FR-013).
/// </summary>
public sealed class MapImageComposerTests
{
    private static readonly GeoPoint Center = new(25.2, 55.3);

    /// <summary>The crop the catalogue declares as the default.</summary>
    private const int DefaultCrop = 32;

    private static MapImageComposer Composer(int maxImageBytes = 10_485_760) =>
        new(Options.Create(new MapCaptureOptions { MaxImageBytes = maxImageBytes }),
            NullLogger<MapImageComposer>.Instance);

    private static byte[] Png(IMagickColor<byte> color, int width, int height)
    {
        using var image = new MagickImage(color, (uint)width, (uint)height);
        return image.ToByteArray(MagickFormat.Png);
    }

    private static byte[] Rgba(byte[] png, out int width, out int height)
    {
        using var image = new MagickImage(png);
        width = (int)image.Width;
        height = (int)image.Height;
        return image.GetPixels().ToByteArray(PixelMapping.RGBA)
            ?? throw new InvalidDataException("no pixels");
    }

    private static (byte R, byte G, byte B) PixelAt(byte[] rgba, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return (rgba[offset], rgba[offset + 1], rgba[offset + 2]);
    }

    [Fact]
    public void Compose_ShouldCropExactlyTheRequestedPixelsFromEveryEdge()
    {
        // Red field with a blue border on every edge; after the crop no blue can remain.
        using var source = new MagickImage(MagickColors.Red, 1280, 1280);
        new Drawables().FillColor(MagickColors.Blue)
            .Rectangle(0, 0, DefaultCrop - 1, 1279)
            .Rectangle(1280 - DefaultCrop, 0, 1279, 1279)
            .Rectangle(0, 0, 1279, DefaultCrop - 1)
            .Rectangle(0, 1280 - DefaultCrop, 1279, 1279)
            .Draw(source);

        var result = Composer().Compose(source.ToByteArray(MagickFormat.Png), Center, 16, []);

        result.IsSuccess.Should().BeTrue();
        result.Width.Should().Be(1280 - (DefaultCrop * 2));
        result.Height.Should().Be(1280 - (DefaultCrop * 2));

        var rgba = Rgba(result.Png!, out var width, out var height);
        PixelAt(rgba, width, 0, 0).Should().Be(((byte)255, (byte)0, (byte)0));
        PixelAt(rgba, width, width - 1, 0).Should().Be(((byte)255, (byte)0, (byte)0));
        PixelAt(rgba, width, 0, height - 1).Should().Be(((byte)255, (byte)0, (byte)0));
        PixelAt(rgba, width, width - 1, height - 1).Should().Be(((byte)255, (byte)0, (byte)0));
    }

    [Fact]
    public void Compose_ShouldCropNothing_WhenTheCropIsZero()
    {
        var result = Composer().Compose(Png(MagickColors.Red, 1280, 1280), Center, 16, [], cropPerEdge: 0);

        result.IsSuccess.Should().BeTrue();
        result.Width.Should().Be(1280);
        result.Height.Should().Be(1280);
    }

    [Fact]
    public void Compose_ShouldRestoreTheContrastAndColourOfAToneMappedImage()
    {
        // A washed-out mid-tone: what a web tone curve leaves behind. The correction must pull
        // the black point back down and the saturation back up.
        using var painted = new MagickImage(MagickColor.FromRgb(180, 172, 160), 256, 256);

        var corrected = Composer().Compose(painted.ToByteArray(MagickFormat.Png), Center, 16, [], 0, enhanceColour: true);
        var untouched = Composer().Compose(painted.ToByteArray(MagickFormat.Png), Center, 16, [], 0, enhanceColour: false);

        corrected.IsSuccess.Should().BeTrue();
        untouched.IsSuccess.Should().BeTrue();

        var correctedPixel = PixelAt(Rgba(corrected.Png!, out var cw, out _), cw, 10, 10);
        var untouchedPixel = PixelAt(Rgba(untouched.Png!, out var uw, out _), uw, 10, 10);

        untouchedPixel.Should().Be(((byte)180, (byte)172, (byte)160), "without the correction the image is untouched");
        correctedPixel.R.Should().BeLessThan(untouchedPixel.R, "the tone-mapped black point is pulled back down");
        (correctedPixel.R - correctedPixel.B).Should().BeGreaterThan(
            untouchedPixel.R - untouchedPixel.B, "the colour the tone curve drained is restored");
    }

    [Fact]
    public void Compose_ShouldReturnImageTooLarge_WhenTheEncodedPngExceedsTheCap()
    {
        var result = Composer(maxImageBytes: 10)
            .Compose(Png(MagickColors.Red, 1280, 1280), Center, 16, []);

        result.FailureReason.Should().Be(MapCaptureFailureReason.ImageTooLarge);
    }

    [Fact]
    public void Compose_ShouldReturnCropTooSmall_WhenTheImageCannotAffordTheCrop()
    {
        var result = Composer().Compose(Png(MagickColors.Red, 20, 20), Center, 16, []);

        result.FailureReason.Should().Be(MapCaptureFailureReason.CropTooSmall);
    }

    [Fact]
    public void Compose_ShouldDrawFootprintRingsInsideTheFrame()
    {
        var (west, south, east, north) = StaticMapFraming.CoveredBounds(Center, 16);
        var westInner = west + (east - west) * 0.25;
        var eastInner = west + (east - west) * 0.75;
        var southInner = south + (north - south) * 0.25;
        var northInner = south + (north - south) * 0.75;

        var footprint = new BuildingFootprint(
            "b1",
            [
                new GeoPoint(southInner, westInner),
                new GeoPoint(southInner, eastInner),
                new GeoPoint(northInner, eastInner),
                new GeoPoint(northInner, westInner),
            ],
            10,
            BuildingHeightProvenance.Known,
            "Test",
            false);

        var result = Composer().Compose(Png(MagickColors.White, 1280, 1280), Center, 16, [footprint]);

        result.IsSuccess.Should().BeTrue();
        var rgba = Rgba(result.Png!, out var width, out var height);
        var strokePixels = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (PixelAt(rgba, width, x, y) == ((byte)255, (byte)0, (byte)0))
                {
                    strokePixels++;
                }
            }
        }

        strokePixels.Should().BeGreaterThan(100, "a 2 px stroke around a mid-frame rectangle leaves a visible trail");
    }

    [Fact]
    public async Task Compose_ShouldNotShareStateBetweenConcurrentCalls()
    {
        var red = Png(MagickColors.Red, 1280, 1280);
        var green = Png(MagickColors.Green, 1280, 1280);
        var composer = Composer();

        var results = await Task.WhenAll(
            Task.Run(() => composer.Compose(red, Center, 16, [])),
            Task.Run(() => composer.Compose(green, Center, 16, [])));

        results.Should().OnlyContain(r => r.IsSuccess);
        PixelAt(Rgba(results[0].Png!, out var w0, out _), w0, 0, 0).Should().Be(((byte)255, (byte)0, (byte)0));
        PixelAt(Rgba(results[1].Png!, out var w1, out _), w1, 0, 0).Should().Be(((byte)0, (byte)128, (byte)0));
    }
}
