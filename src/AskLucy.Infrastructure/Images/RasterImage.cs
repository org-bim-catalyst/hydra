using ImageMagick;

namespace AskLucy.Infrastructure.Images;

/// <summary>
/// A decoded 8-bit RGBA raster. Magick.NET (Apache-2.0) does the decoding/encoding; the pixel
/// analysis itself runs over a plain byte array so the boundary and building thresholds stay easy
/// to read and test. Replaces ImageSharp, whose free line has no patched release for the current
/// advisories and whose 4.x line requires a commercial license.
/// </summary>
internal sealed class RasterImage
{
    public int Width { get; }

    public int Height { get; }

    /// <summary>Row-major RGBA, four bytes per pixel.</summary>
    public byte[] Rgba { get; }

    private RasterImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public static RasterImage Decode(byte[] bytes)
    {
        using var image = new MagickImage(bytes);
        return FromImage(image);
    }

    public static RasterImage FromRgba(byte[] rgba, int width, int height) => new(width, height, rgba);

    /// <summary>The image's dimensions without keeping its pixels (only the size is needed for the mask overlay).</summary>
    public static (int Width, int Height) ReadSize(byte[] bytes)
    {
        using var image = new MagickImage(bytes);
        return ((int)image.Width, (int)image.Height);
    }

    /// <summary>
    /// Decodes a greyscale mask and scales it to <paramref name="width"/> × <paramref name="height"/>
    /// (Google sizes the mask to the box's own span, not the source image's). Returns one byte per pixel.
    /// </summary>
    public static byte[] DecodeGreyResized(byte[] bytes, int width, int height)
    {
        using var image = new MagickImage(bytes);
        image.Resize(new MagickGeometry((uint)width, (uint)height));

        var rgba = ToRgba(image);
        var grey = new byte[width * height];
        for (var i = 0; i < grey.Length; i++)
        {
            grey[i] = rgba[i * 4];
        }

        return grey;
    }

    public byte[] EncodeJpeg(int quality = 90) => Encode(MagickFormat.Jpeg, quality);

    public byte[] EncodePng() => Encode(MagickFormat.Png, quality: null);

    private byte[] Encode(MagickFormat format, int? quality)
    {
        using var image = new MagickImage(
            Rgba,
            new PixelReadSettings((uint)Width, (uint)Height, StorageType.Char, PixelMapping.RGBA));
        image.Format = format;
        if (quality is { } value)
        {
            image.Quality = (uint)value;
        }

        return image.ToByteArray();
    }

    private static RasterImage FromImage(MagickImage image) =>
        new((int)image.Width, (int)image.Height, ToRgba(image));

    private static byte[] ToRgba(MagickImage image) =>
        image.GetPixels().ToByteArray(PixelMapping.RGBA)
        ?? throw new InvalidDataException("The image has no pixels.");
}

/// <summary>An 8-bit RGB colour for the diagnostic renderer.</summary>
internal readonly record struct Rgb(byte R, byte G, byte B);
