using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AskLucy.Infrastructure.Boundaries;

/// <summary>
/// Draws a closed pixel-space ring back onto a copy of the source image, so a human can visually
/// verify a deterministically-extracted outline the same way the "draw" diagnostic's own generated
/// image is verified. A plain Bresenham line plot — <c>SixLabors.ImageSharp.Drawing</c> (the shape
/// package) isn't a dependency here, only core ImageSharp is, and a few pixels of line width for a
/// closed polygon doesn't need more than that.
/// </summary>
internal static class PixelRingRenderer
{
    public static byte[] DrawRingOnImage(byte[] originalImageBytes, IReadOnlyList<(int X, int Y)> ring, Rgba32 color, int thickness = 3)
    {
        using var image = Image.Load<Rgba32>(originalImageBytes);

        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            DrawLine(image, a, b, color, thickness);
        }

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static void DrawLine(Image<Rgba32> image, (int X, int Y) from, (int X, int Y) to, Rgba32 color, int thickness)
    {
        var x0 = from.X; var y0 = from.Y;
        var x1 = to.X; var y1 = to.Y;
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true)
        {
            PlotThick(image, x0, y0, color, thickness);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void PlotThick(Image<Rgba32> image, int centerX, int centerY, Rgba32 color, int thickness)
    {
        var half = thickness / 2;
        for (var dx = -half; dx <= half; dx++)
        {
            for (var dy = -half; dy <= half; dy++)
            {
                var x = centerX + dx;
                var y = centerY + dy;
                if (x >= 0 && y >= 0 && x < image.Width && y < image.Height)
                {
                    image[x, y] = color;
                }
            }
        }
    }
}
